using System.Security.Claims;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    public IReadOnlyDictionary<string, string?> BuildChallengePropertyItems(string authenticationScheme) =>
        new Dictionary<string, string?>(
            signInManager.ConfigureExternalAuthenticationProperties(authenticationScheme, redirectUrl: null).Items);

    public async Task<GoogleSignInResult> CompleteSignInAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return new GoogleSignInResult(GoogleSignInOutcome.ExternalLoginInfoMissing, null);
        }

        // Fast path: this Google identity (provider + immutable `sub`, per docs/THREAT_MODEL.md §6)
        // is already linked to an Account. ExternalLoginSignInAsync re-runs the same lockout/
        // confirmed-account checks the email/password path uses. bypassTwoFactor is safe to set here
        // because a Google-linked identity can never belong to the Super Admin principal
        // (docs/adr/0002: "Super Admin public registration veya Google callback ile oluşamaz"), so
        // there is no second factor this could ever skip in practice.
        var signInResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            // docs/THREAT_MODEL.md T11: AuthAccountService.LoginAsync rejects a banned account
            // before ever completing sign-in. ExternalLoginSignInAsync above has no equivalent ban
            // check built into it, so it must be applied explicitly here too — the caller must never
            // receive a "signed in" result for a banned account, including through this fast path.
            var linkedUser = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linkedUser is not null && await IsBannedAsync(linkedUser.Id, cancellationToken))
            {
                await signInManager.SignOutAsync();
                return new GoogleSignInResult(GoogleSignInOutcome.Banned, null);
            }

            return new GoogleSignInResult(GoogleSignInOutcome.Succeeded, ResolveSafeReturnUrl(returnUrl));
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
        // (EmailAlreadyRegisteredWithoutLinking below) or to create a new one
        // (CreateAccountAndSignInAsync). Checked once here, fail-closed, before either use. Mirrors
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
            // itself. The exact linking UX is an unspecified product decision (not in
            // docs/PRODUCT.md or any accepted ADR); this foundation milestone deliberately does not
            // invent one.
            return new GoogleSignInResult(GoogleSignInOutcome.EmailAlreadyRegisteredWithoutLinking, null);
        }

        return await CreateAccountAndSignInAsync(info, email, cancellationToken, returnUrl);
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
            .Join(dbContext.BanRecords, account => account.Id, ban => ban.AccountId, (account, ban) => ban)
            .AnyAsync(ban => ban.RevokedAt == null, cancellationToken);

    /// <summary>
    /// No ban check is needed on this path: a brand-new Google signup creates a brand-new
    /// <c>Account</c> row with a freshly generated id inside this same method, so no
    /// <c>BanRecord</c> could possibly already reference it — there is nothing in the database yet
    /// to check against. <see cref="IsEmailVerifiedByProvider"/> has already been checked by the
    /// caller (<see cref="CompleteSignInAsync"/>) before this method is ever reached.
    /// </summary>
    private async Task<GoogleSignInResult> CreateAccountAndSignInAsync(
        ExternalLoginInfo info, string email, CancellationToken cancellationToken, string? returnUrl)
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

        // AccountType is always Individual here, mirroring AuthAccountService.RegisterAsync: an
        // Organization signup flow is unspecified product behavior and out of this milestone's
        // scope.
        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name);
        var account = Account.Create(
            Guid.NewGuid(),
            user.Id,
            AccountType.Individual,
            string.IsNullOrWhiteSpace(displayName) ? email : displayName,
            clock.UtcNow);

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // A brand-new external login has never had two-factor configured (Google-created accounts
        // can never be the Super Admin principal — docs/adr/0002), so there is no two-factor branch
        // to consider here, unlike AuthAccountService.LoginAsync.
        await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);

        return new GoogleSignInResult(GoogleSignInOutcome.Succeeded, ResolveSafeReturnUrl(returnUrl));
    }

    /// <summary>
    /// docs/THREAT_MODEL.md §6: "returnUrl yalnız local allowlist içinden seçilir; open redirect
    /// yoktur". Only an app-relative path (no scheme/host, no protocol-relative "//" prefix) is ever
    /// accepted; anything else falls back to the site root. The browser is redirected to this path
    /// under the frontend's own trusted base URL (<see cref="PublicWebOptions.BaseUrl"/>) — the same
    /// trusted-configuration source <c>AuthAccountService</c> uses for email links — rather than a
    /// path relative to this API's own origin, since the frontend and API are not guaranteed to share
    /// one origin (docs/ARCHITECTURE.md §2's CORS allowlist already assumes they may not).
    /// </summary>
    private string ResolveSafeReturnUrl(string? returnUrl)
    {
        const string fallback = "/";

        var safeRelativePath =
            string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.Contains('\\', StringComparison.Ordinal) ||
            Uri.TryCreate(returnUrl, UriKind.Absolute, out _)
                ? fallback
                : returnUrl;

        return publicWebOptions.Value.BaseUrl.TrimEnd('/') + safeRelativePath;
    }
}
