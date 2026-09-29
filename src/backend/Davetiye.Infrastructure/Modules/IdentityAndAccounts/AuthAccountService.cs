using System.Net;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    IOptions<PublicWebOptions> publicWebOptions) : IAuthAccountService
{
    public async Task<RegisterAccountResult> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return new RegisterAccountResult(RegisterAccountOutcome.InvalidRequest, ["Email, password and display name are required."]);
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

        // AccountType is always Individual here: an Organization signup flow is unspecified product
        // behavior and explicitly out of this milestone's scope (docs/PHASE_0_BASELINE.md §12 PD-*
        // does not cover this, and no accepted product document describes an Organization signup
        // UX). Only Individual accounts may be created through public registration.
        var account = Account.Create(
            Guid.NewGuid(),
            user.Id,
            AccountType.Individual,
            request.DisplayName,
            clock.UtcNow);

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmationLink = BuildLink("/auth/confirm-email", user.Id, token);

        await emailSender.SendAsync(
            request.Email,
            EmailNotificationKinds.EmailConfirmation,
            new Dictionary<string, string>
            {
                ["displayName"] = request.DisplayName,
                ["confirmationLink"] = confirmationLink,
            },
            cancellationToken);

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
            return new LoginResult(LoginOutcome.LockedOut);
        }

        if (passwordCheck.IsNotAllowed)
        {
            // With IdentityOptions.SignIn.RequireConfirmedAccount = true, Identity's own
            // PreSignInCheck rejects an unconfirmed account with SignInResult.IsNotAllowed before it
            // even looks at the password, so this branch is reached for both a correct and an
            // incorrect password attempt against an unconfirmed account.
            //
            // Judgment call: unlike "email doesn't exist" (collapsed into InvalidCredentials above
            // to avoid account enumeration), revealing "this account exists but is unconfirmed" as a
            // distinct outcome is not a meaningful enumeration leak here. The caller already had to
            // know/guess this exact email to reach this branch at all (FindByEmailAsync already
            // found a real user), so a third party who does NOT know whether the email is registered
            // learns nothing new from this response that they could not already learn by attempting
            // /auth/register with the same address (which already answers "already registered" via
            // RegisterAccountOutcome.EmailAlreadyRegistered). Meanwhile a legitimate user who forgot
            // to confirm their own email gets an actionable response instead of an indistinguishable
            // "wrong password", which is a real usability win. We therefore return a distinct
            // outcome rather than collapsing it into InvalidCredentials.
            return new LoginResult(LoginOutcome.EmailNotConfirmed);
        }

        if (!passwordCheck.Succeeded)
        {
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var isBanned = await dbContext.Accounts
            .Where(account => account.IdentityUserId == user.Id)
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

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildLink("/auth/reset-password", user.Id, token);

        await emailSender.SendAsync(
            request.Email,
            EmailNotificationKinds.PasswordReset,
            new Dictionary<string, string> { ["resetLink"] = resetLink },
            cancellationToken);
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
        var encodedToken = WebUtility.UrlEncode(token);

        return $"{baseUrl}{relativePath}?userId={userId}&token={encodedToken}";
    }
}
