namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Route-class rate limit policy configuration (docs/THREAT_MODEL.md T14 password-reset
/// enumeration/brute force, T16 bot spam). Applied per-IP at minimum, per this milestone's scope;
/// a later milestone may add a resource/token-keyed dimension on top of this.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimits";

    public RouteRateLimit Register { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit Login { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit PasswordResetRequest { get; init; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public RouteRateLimit EmailConfirmation { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit PasswordResetConfirm { get; init; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public RouteRateLimit AntiforgeryToken { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>
    /// Guards the TOTP/recovery-code two-factor login-completion endpoint. A 6-digit authenticator
    /// code has only 10^6 possibilities; combined with the account-lockout counter
    /// (<see cref="DavetiyeSignInManager.TwoFactorSignInWithAmrClaimAsync"/> already calls
    /// <c>UserManager.AccessFailedAsync</c> on a wrong code), this keeps a brute-force attempt slow
    /// even before lockout kicks in.
    /// </summary>
    public RouteRateLimit TwoFactorLoginComplete { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>
    /// Guards the TOTP verify-and-enable endpoint (enrollment confirmation). Same brute-force shape
    /// as <see cref="TwoFactorLoginComplete"/> (a 6-digit code) but reached with only a first-factor
    /// Super Admin session instead of a completed login. Account-level lockout accounting
    /// (<c>AdminMfaService.VerifyAndEnableAsync</c>) backs this up; this is the per-IP route-class
    /// layer, matching <see cref="TwoFactorLoginComplete"/>'s reasoning.
    /// </summary>
    public RouteRateLimit AdminMfaVerify { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public sealed class RouteRateLimit
    {
        public int PermitLimit { get; init; }

        public int WindowSeconds { get; init; }
    }
}
