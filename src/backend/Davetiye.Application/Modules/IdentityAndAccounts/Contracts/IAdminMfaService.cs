namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// TOTP-authenticator enrollment and two-factor login completion for the Super Admin principal
/// (docs/PHASE_0_BASELINE.md §7, docs/adr/0002). Enroll/VerifyAndEnable are only ever reachable by a
/// caller who already holds the Super Admin claim (enforced by the "SuperAdminOnly" authorization
/// policy at the endpoint, not by this interface) — MFA is not a general Creator-facing feature.
/// </summary>
public interface IAdminMfaService
{
    /// <summary>
    /// Issues a fresh authenticator key for the given Identity user. Safe to call repeatedly before
    /// <see cref="VerifyAndEnableAsync"/> succeeds; each call simply replaces the pending key.
    /// </summary>
    Task<EnrollMfaResult> EnrollAsync(Guid identityUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a TOTP code against the key issued by <see cref="EnrollAsync"/>, and on success
    /// enables two-factor for the account and issues a fresh batch of one-time recovery codes.
    /// </summary>
    Task<VerifyMfaResult> VerifyAndEnableAsync(Guid identityUserId, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a login that <see cref="IAuthAccountService.LoginAsync"/> left pending because the
    /// account has two-factor enabled (docs/adr/0002's MFA-complete requirement). A valid TOTP code
    /// or one-time recovery code finishes the sign-in with the per-session "amr=mfa" marker the
    /// "MfaComplete" authorization policy checks for.
    /// </summary>
    Task<CompleteTwoFactorLoginResult> CompleteTwoFactorLoginAsync(
        CompleteTwoFactorLoginRequest request, CancellationToken cancellationToken);
}
