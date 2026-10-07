using System.Security.Claims;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Implements <see cref="IGoogleSignInService"/>. Runs after the Google authentication middleware has
/// already exchanged the authorization code, validated state/correlation and established the
/// <c>IdentityConstants.ExternalScheme</c> cookie (<c>SecurityServiceCollectionExtensions</c> wires
/// the Google handler with that as its <c>SignInScheme</c>) — this service only ever reads the
/// already-validated result via <see cref="SignInManager{TUser}.GetExternalLoginInfoAsync"/>, never
/// any raw OAuth material itself.
///
/// Mirrors <see cref="AuthAccountService.RegisterAsync"/>'s transactional Identity-user-plus-Account
/// creation pattern for a genuinely new sign-in, and never silently links/merges an existing
/// email/password account (docs/THREAT_MODEL.md §6).
/// </summary>
public sealed class GoogleSignInService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    DavetiyeDbContext dbContext,
    IClock clock,
    IOptions<PublicWebOptions> publicWebOptions) : IGoogleSignInService
{
    private const string AccountTypeProperty = "davetiye:account_type";
    private const string ReturnPathProperty = "davetiye:return_path";
    private const string ServiceNoticeAcknowledgedProperty = "davetiye:service_notice_acknowledged";
    private const string MarketingOptInProperty = "davetiye:marketing_opt_in";

    public IReadOnlyDictionary<string, string?>? BuildChallengePropertyItems(
        string authenticationScheme,
        string? accountType,
        string? returnUrl,
        bool serviceNoticeAcknowledged,
        bool marketingOptIn)
    {
        if (!TryParseAccountType(accountType, out var parsedAccountType))
        {
            return null;
        }

        var items = new Dictionary<string, string?>(
            signInManager.ConfigureExternalAuthenticationProperties(authenticationScheme, redirectUrl: null).Items)
        {
            [AccountTypeProperty] = parsedAccountType.ToString(),
            [ReturnPathProperty] = ResolveSafeReturnPath(returnUrl),
            [ServiceNoticeAcknowledgedProperty] = serviceNoticeAcknowledged ? "true" : "false",
            [MarketingOptInProperty] = marketingOptIn ? "true" : "false",
        };

        return items;
    }

    public async Task<GoogleSignInResult> CompleteSignInAsync(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return new GoogleSignInResult(GoogleSignInOutcome.ExternalLoginInfoMissing, null);
        }

        // Fast path: this Google identity (provider + immutable `sub`, per docs/THREAT_MODEL.md §6)
        // is already linked to an Account. Looked up and checked BEFORE calling
        // ExternalLoginSignInAsync below (rather than after, as a prior revision of this method did
        // for the ban check) because that call's bypassTwoFactor: true unconditionally establishes a
        // signed-in cookie/session, skipping any second factor entirely — by the time it returns it
        // is too late to have "never signed this principal in" be true.
        //
        // The Super Admin check is defense-in-depth for docs/adr/0002's "a Super Admin principal
        // never authenticates via Google" invariant: previously this was a code-unenforced
        // assumption ("a Google-linked identity can never belong to a Super Admin" — true only
        // because no linking flow existed to attach a Google login to an existing Account's Identity
        // user). Now that ConfirmLinkAsync exists, that assumption is enforced here in code instead
        // of merely relied upon.
        var linkedUser = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linkedUser is not null)
        {
            if (await HasSuperAdminClaimAsync(linkedUser))
            {
                return new GoogleSignInResult(GoogleSignInOutcome.SuperAdminGoogleSignInNotAllowed, null);
            }

            // docs/THREAT_MODEL.md T11: AuthAccountService.LoginAsync rejects a banned account before
            // ever completing sign-in. ExternalLoginSignInAsync below has no equivalent ban check
            // built into it, so it must be applied explicitly here too, before establishing any
            // session — the caller must never receive a "signed in" result for a banned account,
            // including through this fast path.
            if (await IsBannedAsync(linkedUser.Id, cancellationToken))
            {
                return new GoogleSignInResult(GoogleSignInOutcome.Banned, null);
            }
        }

        // ExternalLoginSignInAsync re-runs the same lockout/confirmed-account checks the
        // email/password path uses. bypassTwoFactor is safe to set here because the checks above
        // have already ruled out a Super-Admin-claimed linked identity — the only principal this
        // milestone's Identity setup ever enables two-factor for (docs/adr/0002) — for both possible
        // outcomes of this call (linkedUser is not null and cleared the check above, or linkedUser is
        // null and there is no existing linked login at all yet).
        var signInResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            return new GoogleSignInResult(GoogleSignInOutcome.Succeeded, ResolveSafeReturnUrl(ReadReturnPath(info)));
        }

        if (signInResult.IsLockedOut || signInResult.IsNotAllowed)
        {
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        // No existing login row for this provider+key. Look at the email Google's consent screen
        // returned to decide whether this is a genuinely new signup or a collision with an existing
        // email/password account.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        // docs/THREAT_MODEL.md §6: "Unverified veya yalnız eşleşen email ile hesap merge edilmez" —
        // an unverified email must never be trusted, whether to match it against an existing account
        // (AccountLinkRequired below) or to create a new one (CreateAccountAndSignInAsync). Checked
        // once here, fail-closed, before either use. Mirrors
        // RequireConfirmedAccount's email-squatting mitigation for the password path: an attacker
        // presenting an unverified Google identity must not be able to permanently occupy an email
        // address.
        if (!IsEmailVerifiedByProvider(info.Principal))
        {
            return new GoogleSignInResult(GoogleSignInOutcome.EmailNotVerifiedByProvider, null);
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            // docs/THREAT_MODEL.md §6: "Unverified veya yalnız eşleşen email ile hesap merge
            // edilmez" — never silently link Google to an account that did not initiate the link
            // itself. Per docs/ROADMAP.md §4a's 2026-09-29 decision, the frontend surfaces an
            // explicit "would you like to link?" prompt from AccountLinkRequired; the caller must
            // then log in with their existing password and call ConfirmLinkAsync to actually attach
            // this Google identity.
            //
            // Same defense-in-depth as the already-linked fast path above: a Super Admin identity
            // (found here by email, since Super Admin never has a Domain Account —
            // AdminBootstrapRunner refuses to grant the claim to a user who already has one) must
            // never be offered Google linking at all.
            if (await HasSuperAdminClaimAsync(existingUser))
            {
                return new GoogleSignInResult(GoogleSignInOutcome.SuperAdminGoogleSignInNotAllowed, null);
            }

            return new GoogleSignInResult(
                GoogleSignInOutcome.AccountLinkRequired,
                ResolveAccountLinkUrl(ReadReturnPath(info)));
        }

        if (info.AuthenticationProperties is null ||
            !info.AuthenticationProperties.Items.TryGetValue(AccountTypeProperty, out var accountTypeValue) ||
            !TryParseAccountType(accountTypeValue, out var accountType))
        {
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        if (!info.AuthenticationProperties.Items.TryGetValue(ServiceNoticeAcknowledgedProperty, out var serviceNoticeValue) ||
            !bool.TryParse(serviceNoticeValue, out var serviceNoticeAcknowledged) ||
            !info.AuthenticationProperties.Items.TryGetValue(MarketingOptInProperty, out var marketingOptInValue) ||
            !bool.TryParse(marketingOptInValue, out var marketingOptIn))
        {
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        if (!serviceNoticeAcknowledged)
        {
            return new GoogleSignInResult(GoogleSignInOutcome.ServiceNoticeAcknowledgementRequired, null);
        }

        return await CreateAccountAndSignInAsync(
            info, email, accountType, marketingOptIn, cancellationToken, ReadReturnPath(info));
    }

    /// <summary>
    /// The second, explicit step of the M1 account-linking flow — see this interface method's doc
    /// comment on <see cref="IGoogleSignInService"/> for the full flow description.
    /// </summary>
    public async Task<GoogleAccountLinkResult> ConfirmLinkAsync(
        Guid identityUserId,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        var currentUser = await userManager.FindByIdAsync(identityUserId.ToString());
        if (currentUser is null)
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.Failed);
        }

        if (string.IsNullOrWhiteSpace(currentPassword) ||
            !await userManager.CheckPasswordAsync(currentUser, currentPassword))
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.InvalidPassword);
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.NoPendingExternalLogin);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || !IsEmailVerifiedByProvider(info.Principal))
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.Failed);
        }

        // Scopes this endpoint to exactly the same-email collision it exists to resolve — never a
        // general "link any Google account you own to any Creator account" feature, which would be a
        // materially different (and unreviewed) product surface.
        if (!string.Equals(email, currentUser.Email, StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.EmailMismatch);
        }

        // Defense-in-depth (docs/adr/0002): re-checked here even though CompleteSignInAsync already
        // refuses to ever offer linking to a Super Admin identity in the first place, because this
        // endpoint is an independently reachable API surface (the caller only needs any authenticated
        // session plus a pending external login, not a prior AccountLinkRequired result).
        if (await HasSuperAdminClaimAsync(currentUser))
        {
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.SuperAdminLinkingNotAllowed);
        }

        IdentityResult addLoginResult;
        try
        {
            addLoginResult = await userManager.AddLoginAsync(currentUser, info);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation
                  })
        {
            // Two link confirmations can race with the same still-valid external cookie. The
            // asp_net_user_logins primary key is the final authority; translate its unique
            // violation into the same safe conflict outcome as Identity's ordinary
            // LoginAlreadyAssociated result instead of leaking a 500/SQL detail.
            return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.AlreadyLinkedToAnotherAccount);
        }

        if (!addLoginResult.Succeeded)
        {
            var alreadyLinkedElsewhere = addLoginResult.Errors.Any(error =>
                error.Code.Contains("LoginAlreadyAssociated", StringComparison.Ordinal));

            return new GoogleAccountLinkResult(
                alreadyLinkedElsewhere
                    ? GoogleAccountLinkOutcome.AlreadyLinkedToAnotherAccount
                    : GoogleAccountLinkOutcome.Failed);
        }

        // Consumes the pending External-scheme cookie so this same completed Google OAuth round-trip
        // cannot be read back and replayed into a second AddLoginAsync call once it has already been
        // used to link.
        await signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);

        return new GoogleAccountLinkResult(GoogleAccountLinkOutcome.Succeeded);
    }

    /// <summary>
    /// Google's raw "email_verified" JSON boolean, mapped by
    /// <c>SecurityServiceCollectionExtensions.AddGoogleAuthentication</c> into a
    /// <see cref="GoogleClaimTypes.EmailVerified"/> claim. Absent or unparsable is treated the same
    /// as "false" (fail closed).
    /// </summary>
    private static bool IsEmailVerifiedByProvider(ClaimsPrincipal principal) =>
        bool.TryParse(principal.FindFirstValue(GoogleClaimTypes.EmailVerified), out var verified) && verified;

    /// <summary>
    /// Mirrors <see cref="AuthAccountService.LoginAsync"/>'s ban query against the same
    /// <c>BanRecords</c> table (M5A schema), joined through <c>Accounts</c> by
    /// <c>IdentityUserId</c> — an unrevoked (<c>RevokedAt == null</c>) record means the account is
    /// currently banned.
    /// </summary>
    private async Task<bool> IsBannedAsync(Guid identityUserId, CancellationToken cancellationToken) =>
        await dbContext.Accounts
            .Where(account => account.IdentityUserId == identityUserId)
            .AnyAsync(account => account.DeletionStartedAtUtc != null ||
                dbContext.BanRecords.Any(ban => ban.AccountId == account.Id && ban.RevokedAt == null), cancellationToken);

    /// <summary>
    /// Mirrors <see cref="AdminBootstrapRunner"/>'s own Super Admin claim check
    /// (<c>SuperAdminClaimNames.SuperAdmin</c>/<c>SuperAdminClaimValue</c>) — the single source of
    /// truth for "does this Identity user hold the Super Admin claim" used by both the Google
    /// sign-in fast path and the explicit account-linking confirmation.
    /// </summary>
    private async Task<bool> HasSuperAdminClaimAsync(ApplicationUser user)
    {
        var claims = await userManager.GetClaimsAsync(user);
        return claims.Any(claim =>
            claim.Type == SuperAdminClaimNames.SuperAdmin && claim.Value == SuperAdminClaimNames.SuperAdminClaimValue);
    }

    /// <summary>
    /// No ban check is needed on this path: a brand-new Google signup creates a brand-new
    /// <c>Account</c> row with a freshly generated id inside this same method, so no
    /// <c>BanRecord</c> could possibly already reference it — there is nothing in the database yet
    /// to check against. <see cref="IsEmailVerifiedByProvider"/> has already been checked by the
    /// caller (<see cref="CompleteSignInAsync"/>) before this method is ever reached.
    /// </summary>
    private async Task<GoogleSignInResult> CreateAccountAndSignInAsync(
        ExternalLoginInfo info,
        string email,
        AccountType accountType,
        bool marketingOptIn,
        CancellationToken cancellationToken,
        string returnPath)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            // Google already verified ownership of this address as part of the OAuth consent flow;
            // there is no separate confirmation link to send for an account with no local password.
            EmailConfirmed = true,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new GoogleSignInResult(GoogleSignInOutcome.Failed, null);
        }

        // accountType is the caller's explicit choice from the challenge request. It crossed the
        // provider round-trip only inside data-protected AuthenticationProperties and was parsed
        // again immediately before this first-time account creation. Existing-account sign-in and
        // linking never mutate an Account's type.
        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name);
        var account = Account.Create(
            Guid.NewGuid(),
            user.Id,
            accountType,
            string.IsNullOrWhiteSpace(displayName) ? email : displayName,
            clock.UtcNow);

        dbContext.Accounts.Add(account);
        dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
            Guid.NewGuid(), account.Id, AccountConsentKind.ServiceNoticeAcknowledgement, true,
            AccountConsentVersions.ServiceNotice, AccountConsentSource.GoogleSignup, clock.UtcNow));
        dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
            Guid.NewGuid(), account.Id, AccountConsentKind.MarketingPreference, marketingOptIn,
            AccountConsentVersions.MarketingPreference, AccountConsentSource.GoogleSignup, clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // A brand-new external login has never had two-factor configured (Google-created accounts
        // can never be the Super Admin principal — docs/adr/0002), so there is no two-factor branch
        // to consider here, unlike AuthAccountService.LoginAsync.
        await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);

        return new GoogleSignInResult(GoogleSignInOutcome.Succeeded, ResolveSafeReturnUrl(returnPath));
    }

    private static bool TryParseAccountType(string? value, out AccountType accountType) =>
        Enum.TryParse(value, ignoreCase: true, out accountType) && Enum.IsDefined(accountType);

    private static string ReadReturnPath(ExternalLoginInfo info) =>
        info.AuthenticationProperties?.Items.TryGetValue(ReturnPathProperty, out var returnPath) == true
            ? ResolveSafeReturnPath(returnPath)
            : "/";

    private string ResolveAccountLinkUrl(string returnPath) =>
        publicWebOptions.Value.BaseUrl.TrimEnd('/') +
        "/giris/google-baglanti?returnUrl=" + Uri.EscapeDataString(ResolveSafeReturnPath(returnPath));

    /// <summary>
    /// docs/THREAT_MODEL.md §6: "returnUrl yalnız local allowlist içinden seçilir; open redirect
    /// yoktur". Only an app-relative path (no scheme/host, no protocol-relative "//" prefix) is ever
    /// accepted; anything else falls back to the site root. The browser is redirected to this path
    /// under the frontend's own trusted base URL (<see cref="PublicWebOptions.BaseUrl"/>) — the same
    /// trusted-configuration source <c>AuthAccountService</c> uses for email links — rather than a
    /// path relative to this API's own origin, since the frontend and API are not guaranteed to share
    /// one origin (docs/ARCHITECTURE.md §2's CORS allowlist already assumes they may not).
    /// </summary>
    private string ResolveSafeReturnUrl(string? returnUrl) =>
        publicWebOptions.Value.BaseUrl.TrimEnd('/') + ResolveSafeReturnPath(returnUrl);

    private static string ResolveSafeReturnPath(string? returnUrl)
    {
        const string fallback = "/";

        return string.IsNullOrWhiteSpace(returnUrl) ||
               !returnUrl.StartsWith('/') ||
               returnUrl.StartsWith("//", StringComparison.Ordinal) ||
               returnUrl.Contains('\\', StringComparison.Ordinal) ||
               returnUrl.Any(char.IsControl) ||
               !Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            ? fallback
            : returnUrl;
    }
}
