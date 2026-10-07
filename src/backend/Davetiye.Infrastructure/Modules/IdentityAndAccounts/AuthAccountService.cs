using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Implements <see cref="IAuthAccountService"/> for M6a's email/password auth scope. Coordinates
/// two separate EF entities for the same logical signup action
/// (<see cref="ApplicationUser"/> via <see cref="UserManager{TUser}"/>, and Domain's
/// <see cref="Account"/> directly through <see cref="DavetiyeDbContext"/>) inside one explicit
/// database transaction, so a failure creating the Account rolls back the Identity user too rather
/// than leaving an orphaned login with no Account.
///
/// The ban check in <see cref="LoginAsync"/> only runs after a correct password has already been
/// verified (docs/THREAT_MODEL.md/M6a scope note: a banned user's own login attempt may say
/// "banned" because only someone with the right credentials reaches this branch — this is never
/// reused on an unauthenticated path).
/// </summary>
public sealed class AuthAccountService(
    UserManager<ApplicationUser> userManager,
    DavetiyeSignInManager signInManager,
    DavetiyeDbContext dbContext,
    IEmailSender emailSender,
    IClock clock,
    IOptions<PublicWebOptions> publicWebOptions,
    IOptions<EmailTokenOptions> emailTokenOptions,
    ILogger<AuthAccountService> logger) : IAuthAccountService
{
    public async Task<RegisterAccountResult> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.AccountType))
        {
            return new RegisterAccountResult(
                RegisterAccountOutcome.InvalidRequest,
                ["Email, password, display name and account type are required."]);
        }

        if (!request.ServiceNoticeAcknowledged)
        {
            return new RegisterAccountResult(
                RegisterAccountOutcome.InvalidRequest,
                ["The required service notice must be acknowledged."]);
        }

        // docs/ROADMAP.md §4a (2026-09-29): account type is an explicit registration-time choice, not
        // inferred, and Account.Create below has no way to change it afterward. Enum.TryParse alone
        // would accept a purely-numeric string for an out-of-range underlying value (e.g. "99"), so
        // Enum.IsDefined is checked too.
        if (!Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out var accountType) ||
            !Enum.IsDefined(accountType))
        {
            return new RegisterAccountResult(
                RegisterAccountOutcome.InvalidRequest,
                ["Account type must be 'Individual' or 'Organization'."]);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            var errors = createResult.Errors.Select(error => error.Description).ToArray();
            var outcome = createResult.Errors.Any(error =>
                error.Code.Contains("DuplicateUserName", StringComparison.Ordinal) ||
                error.Code.Contains("DuplicateEmail", StringComparison.Ordinal))
                ? RegisterAccountOutcome.EmailAlreadyRegistered
                : RegisterAccountOutcome.InvalidPassword;

            return new RegisterAccountResult(outcome, errors);
        }

        // accountType is the caller's validated, explicit Individual/Organization choice (see the
        // validation above) — docs/ROADMAP.md §4a: never inferred, never convertible afterward.
        var account = Account.Create(
            Guid.NewGuid(),
            user.Id,
            accountType,
            request.DisplayName,
            clock.UtcNow);

        dbContext.Accounts.Add(account);
        dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
            Guid.NewGuid(), account.Id, AccountConsentKind.ServiceNoticeAcknowledgement, true,
            AccountConsentVersions.ServiceNotice, AccountConsentSource.EmailPasswordSignup, clock.UtcNow));
        dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
            Guid.NewGuid(), account.Id, AccountConsentKind.MarketingPreference, request.MarketingOptIn,
            AccountConsentVersions.MarketingPreference, AccountConsentSource.EmailPasswordSignup, clock.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        var tokenCreatedAt = clock.UtcNow;
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmationLink = BuildLink("/auth/confirm-email", user.Id, token);

        await emailSender.SendShortLivedForAccountAsync(account.Id,
            request.Email,
            EmailNotificationKinds.EmailConfirmation,
            new Dictionary<string, string>
            {
                ["displayName"] = request.DisplayName,
                ["confirmationLink"] = confirmationLink,
            },
            tokenCreatedAt.AddMinutes(emailTokenOptions.Value.TokenLifetimeMinutes),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return RegisterAccountResult.Succeeded();
    }

    public async Task<ConfirmEmailResult> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
        {
            return new ConfirmEmailResult(ConfirmEmailOutcome.InvalidRequest);
        }

        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return new ConfirmEmailResult(ConfirmEmailOutcome.InvalidRequest);
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);

        return new ConfirmEmailResult(
            result.Succeeded ? ConfirmEmailOutcome.Succeeded : ConfirmEmailOutcome.InvalidRequest);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            // Intentionally the exact same outcome as a wrong password below: this endpoint never
            // reveals whether the email is registered.
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var passwordCheck = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (passwordCheck.IsLockedOut)
        {
            // Identity maintains its lockout state internally, but exposing it as a distinct result
            // reveals that the submitted address belongs to an account. Keep the public response
            // identical to unknown, unconfirmed, and wrong-password attempts.
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (passwordCheck.IsNotAllowed)
        {
            // Identity's RequireConfirmedAccount rejects before password verification. Keep that
            // state indistinguishable from an unknown address or wrong password to avoid account
            // enumeration through the login endpoint.
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (!passwordCheck.Succeeded)
        {
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var isBanned = await dbContext.Accounts
            .Where(account => account.IdentityUserId == user.Id && account.DeletionStartedAtUtc == null)
            .Join(dbContext.BanRecords, account => account.Id, ban => ban.AccountId, (account, ban) => ban)
            .AnyAsync(ban => ban.RevokedAt == null, cancellationToken);

        if (isBanned)
        {
            return new LoginResult(LoginOutcome.Banned);
        }

        // A brand-new sign-in always establishes a fresh cookie/principal, which is the "rotate
        // after login" behavior docs/THREAT_MODEL.md §5 requires. When the account has two-factor
        // enabled (M6b — currently only the Super Admin principal), this stores the pending
        // two-factor challenge instead of completing sign-in, mirroring every non-2FA account's
        // existing behavior exactly when no second factor is configured.
        var signInOutcome = await signInManager.SignInOrRequireTwoFactorAsync(user, isPersistent: false);

        return signInOutcome.RequiresTwoFactor
            ? new LoginResult(LoginOutcome.RequiresTwoFactor)
            : new LoginResult(LoginOutcome.Succeeded);
    }

    public Task LogoutAsync(CancellationToken cancellationToken) => signInManager.SignOutAsync();

    public async Task RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // No branch on the caller's side: docs/THREAT_MODEL.md §9 requires an account-enumeration-safe
            // response, so this method always completes the same way regardless of whether the user exists.
            return;
        }

        try
        {
            var accountId = await dbContext.Accounts.AsNoTracking()
                .Where(account => account.IdentityUserId == user.Id && account.DeletionStartedAtUtc == null)
                .Select(account => (Guid?)account.Id).SingleOrDefaultAsync(cancellationToken);
            if (accountId is null) return;
            var tokenCreatedAt = clock.UtcNow;
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = BuildLink("/auth/reset-password", user.Id, token);

            await emailSender.SendShortLivedForAccountAsync(accountId.Value,
                request.Email,
                EmailNotificationKinds.PasswordReset,
                new Dictionary<string, string> { ["resetLink"] = resetLink },
                tokenCreatedAt.AddMinutes(emailTokenOptions.Value.TokenLifetimeMinutes),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Preserve the account-enumeration-safe response even when durable enqueue fails. Never
            // log address, link, token, template data, or provider payload.
            logger.LogWarning("Password-reset notification enqueue failed ({FailureType}).", exception.GetType().Name);
        }
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserId) ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return new ResetPasswordResult(ResetPasswordOutcome.InvalidRequest, []);
        }

        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            // Collapsed with "invalid token" below: this outward-facing outcome deliberately does
            // not distinguish "unknown user" from "bad/expired token".
            return new ResetPasswordResult(ResetPasswordOutcome.InvalidRequest, []);
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => error.Description).ToArray();
            var outcome = result.Errors.Any(error => error.Code.Contains("Password", StringComparison.Ordinal))
                ? ResetPasswordOutcome.InvalidPassword
                : ResetPasswordOutcome.InvalidRequest;

            return new ResetPasswordResult(outcome, errors);
        }

        // UserManager.ResetPasswordAsync already updates the security stamp as part of changing the
        // password hash; this explicit call is defense-in-depth so the "reset revokes existing
        // sessions" contract (docs/THREAT_MODEL.md §5) does not silently depend on that framework
        // implementation detail never changing.
        await userManager.UpdateSecurityStampAsync(user);

        return new ResetPasswordResult(ResetPasswordOutcome.Succeeded, []);
    }

    private string BuildLink(string relativePath, Guid userId, string token)
    {
        var baseUrl = publicWebOptions.Value.BaseUrl.TrimEnd('/');
        // Auth bearer material stays in the fragment: browsers never send it in the HTTP request,
        // so reverse-proxy access logs cannot capture it. The SPA consumes and removes it before
        // sending the existing JSON API request.
        var encodedUserId = Uri.EscapeDataString(userId.ToString());
        var encodedToken = Uri.EscapeDataString(token);

        return $"{baseUrl}{relativePath}#userId={encodedUserId}&token={encodedToken}";
    }
}
