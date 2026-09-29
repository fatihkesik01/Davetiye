namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Configurable auth-cookie lifetime/session-revocation behavior. The structural security
/// properties (host-only, Secure, HttpOnly, SameSite=Lax, Path=/, the environment-conditional
/// <c>__Host-</c> name prefix) are deliberately NOT configurable here — docs/THREAT_MODEL.md §5
/// fixes those as non-negotiable, and making them configuration-driven would let a bad deployment
/// config silently weaken the cookie. Only the session lifetime and how often an already-issued
/// cookie's security stamp is re-checked against the database are configurable.
/// </summary>
public sealed class AuthCookieOptions
{
    public const string SectionName = "AuthCookie";

    public int ExpirationHours { get; init; } = 12;

    /// <summary>
    /// How often (in seconds) a request with an already-issued auth cookie re-validates its
    /// principal's security stamp against the current database value
    /// (<see cref="Microsoft.AspNetCore.Identity.SecurityStampValidator"/>). 0 means "validate on
    /// every request", which is the safest default and what makes ban/password/security-stamp
    /// revocation (docs/THREAT_MODEL.md §5) take effect immediately rather than after a delay.
    /// </summary>
    public int SecurityStampValidationIntervalSeconds { get; init; }
}
