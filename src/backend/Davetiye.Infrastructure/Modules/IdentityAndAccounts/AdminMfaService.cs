using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Implements <see cref="IAdminMfaService"/> entirely on top of ASP.NET Core Identity's built-in TOTP
/// support (<c>UserManager.GetAuthenticatorKeyAsync</c>/<c>ResetAuthenticatorKeyAsync</c>,
/// <c>TokenOptions.DefaultAuthenticatorProvider</c>, <c>GenerateNewTwoFactorRecoveryCodesAsync</c>) —
/// no custom TOTP crypto. Which caller may enroll is enforced by the "SuperAdminOnly" authorization
/// policy at the endpoint (Davetiye.Api), not by this service.
/// </summary>
public sealed class AdminMfaService(
    UserManager<ApplicationUser> userManager,
    DavetiyeSignInManager signInManager,
    IOptions<MfaOptions> mfaOptions) : IAdminMfaService
{
    private const string Issuer = "Davetiye";

    public async Task<EnrollMfaResult> EnrollAsync(Guid identityUserId, CancellationToken cancellationToken)
    {
        var user = await FindUserOrThrowAsync(identityUserId);

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return new EnrollMfaResult(AlreadyEnabled: true, SharedKey: null, AuthenticatorUri: null);
        }

        // Always issues a fresh, unconfirmed key. Re-enrolling before VerifyAndEnableAsync succeeds
        // is safe and simply replaces the pending key — 2FA is not actually turned on until
        // VerifyAndEnableAsync confirms a code generated from it.
        await userManager.ResetAuthenticatorKeyAsync(user);
        var unformattedKey = await userManager.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Authenticator key was not generated.");

        // ResetAuthenticatorKeyAsync bumps the Identity security stamp as a side effect. Without
        // refreshing the caller's own cookie here, SecurityStampValidator (M6a's default validates
        // every request) would reject the very next request from this same admin and force an
        // unwanted re-login in the middle of their own enrollment flow.
        await signInManager.RefreshSignInAsync(user);

        var authenticatorUri = BuildAuthenticatorUri(user.Email ?? user.Id.ToString(), unformattedKey);

        return new EnrollMfaResult(AlreadyEnabled: false, unformattedKey, authenticatorUri);
    }

    public async Task<VerifyMfaResult> VerifyAndEnableAsync(
        Guid identityUserId, string code, CancellationToken cancellationToken)
    {
        var user = await FindUserOrThrowAsync(identityUserId);

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return new VerifyMfaResult(VerifyMfaOutcome.AlreadyEnabled, []);
        }

        // Mirrors DavetiyeSignInManager.TwoFactorSignInWithAmrClaimAsync's lockout accounting.
        // Without this, an attacker who reached a first-factor "SuperAdminOnly" session (e.g. a
        // stolen password) could brute-force the pending TOTP secret against this endpoint with no
        // account-level throttling, only the route-level rate limit.
        if (await userManager.IsLockedOutAsync(user))
        {
            return new VerifyMfaResult(VerifyMfaOutcome.LockedOut, []);
        }

        var isValid = await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
        if (!isValid)
        {
            if (userManager.SupportsUserLockout)
            {
                await userManager.AccessFailedAsync(user);
            }

            return new VerifyMfaResult(VerifyMfaOutcome.InvalidCode, []);
        }

        if (userManager.SupportsUserLockout)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user, mfaOptions.Value.RecoveryCodeCount);

        // SetTwoFactorEnabledAsync also bumps the security stamp — same reasoning as EnrollAsync.
        await signInManager.RefreshSignInAsync(user);

        return new VerifyMfaResult(VerifyMfaOutcome.Succeeded, recoveryCodes?.ToArray() ?? []);
    }

    public async Task<CompleteTwoFactorLoginResult> CompleteTwoFactorLoginAsync(
        CompleteTwoFactorLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await signInManager.TwoFactorSignInWithAmrClaimAsync(
            request.Code, request.IsRecoveryCode, isPersistent: false);

        if (result.Succeeded)
        {
            return new CompleteTwoFactorLoginResult(CompleteTwoFactorLoginOutcome.Succeeded);
        }

        if (result.IsLockedOut)
        {
            return new CompleteTwoFactorLoginResult(CompleteTwoFactorLoginOutcome.LockedOut);
        }

        // "No pending two-factor session" and "the code was wrong" both surface as SignInResult.Failed
        // from TwoFactorSignInWithAmrClaimAsync; the caller-facing distinction is not security-relevant
        // here (nothing more can be learned either way), so both collapse to InvalidCode.
        return new CompleteTwoFactorLoginResult(CompleteTwoFactorLoginOutcome.InvalidCode);
    }

    private async Task<ApplicationUser> FindUserOrThrowAsync(Guid identityUserId) =>
        await userManager.FindByIdAsync(identityUserId.ToString())
            ?? throw new InvalidOperationException("Unknown identity user.");

    private static string BuildAuthenticatorUri(string email, string unformattedKey) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}" +
        $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
}
