using System.Security.Claims;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Extends the framework <see cref="SignInManager{TUser}"/> with the two Admin-MFA-specific
/// behaviors M6b needs, without changing how the base type behaves for the plain email/password path
/// M6a already built: (1) exposing the protected two-factor branch of the standard password sign-in
/// flow so <see cref="AuthAccountService.LoginAsync"/> can require a second factor for a 2FA-enabled
/// account while leaving every non-2FA account's sign-in identical to before, and (2) completing that
/// second factor with an explicit per-session "amr=mfa" claim (see
/// <see cref="SuperAdminClaimNames.AuthenticationMethodReference"/>) that a plain password-only
/// sign-in never carries — this is what the "MfaComplete" authorization policy actually checks.
/// </summary>
public sealed class DavetiyeSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>
    /// Exposes the base class's protected two-factor branch of <c>PasswordSignInAsync</c>: signs the
    /// user in directly when they have no enabled second factor (identical outcome to the pre-M6b
    /// behavior), or stores the pending two-factor challenge under
    /// <see cref="IdentityConstants.TwoFactorUserIdScheme"/> and returns
    /// <see cref="SignInResult.TwoFactorRequired"/> instead of completing sign-in when they do.
    /// </summary>
    public Task<SignInResult> SignInOrRequireTwoFactorAsync(ApplicationUser user, bool isPersistent) =>
        SignInOrTwoFactorAsync(user, isPersistent);

    /// <summary>
    /// Completes a pending two-factor challenge (see <see cref="SignInOrRequireTwoFactorAsync"/>)
    /// using ASP.NET Core Identity's own TOTP/recovery-code verification (no custom crypto). Unlike
    /// the base class's own <c>TwoFactorSignInAsync</c>, this signs the completed principal in with an
    /// explicit "amr=mfa" claim, so a later request can prove this specific session actually finished
    /// a second factor rather than only ever having a first-factor-only cookie. A failed attempt
    /// increments the same lockout counter a wrong password would.
    /// </summary>
    public async Task<SignInResult> TwoFactorSignInWithAmrClaimAsync(string code, bool isRecoveryCode, bool isPersistent)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return SignInResult.Failed;
        }

        if (await UserManager.IsLockedOutAsync(user))
        {
            return SignInResult.LockedOut;
        }

        var verified = isRecoveryCode
            ? (await UserManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
            : await UserManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);

        if (!verified)
        {
            if (UserManager.SupportsUserLockout)
            {
                await UserManager.AccessFailedAsync(user);
            }

            return SignInResult.Failed;
        }

        if (UserManager.SupportsUserLockout)
        {
            await UserManager.ResetAccessFailedCountAsync(user);
        }

        await SignInWithClaimsAsync(
            user,
            isPersistent,
            [new Claim(SuperAdminClaimNames.AuthenticationMethodReference, SuperAdminClaimNames.MfaAmrValue)]);

        await Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        return SignInResult.Success;
    }
}
