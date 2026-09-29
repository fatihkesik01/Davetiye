namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Named authorization policy identifiers registered by <c>AddAuthSecurity</c>. Davetiye.Api's
/// composition root (<c>Program.cs</c>) reads these constants and passes the plain string values down
/// into endpoint-mapping extension methods, matching the existing
/// <see cref="AuthRateLimitPolicyNames"/> convention so non-<c>Program.cs</c> Api files never need to
/// reference Davetiye.Infrastructure directly.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>
    /// Requires only the <see cref="SuperAdminClaimNames.SuperAdmin"/> claim. Used to gate the MFA
    /// enrollment endpoints themselves — a freshly bootstrapped Super Admin has not completed a
    /// second factor yet (there is nothing to complete until enrollment finishes), so this policy,
    /// not <see cref="MfaComplete"/>, is what protects those endpoints.
    /// </summary>
    public const string SuperAdminOnly = "SuperAdminOnly";

    /// <summary>
    /// Requires both the <see cref="SuperAdminClaimNames.SuperAdmin"/> claim and the per-session
    /// <see cref="SuperAdminClaimNames.AuthenticationMethodReference"/>="mfa" claim. This is the
    /// reusable contract a later milestone's real Admin business endpoints apply via
    /// <c>RequireAuthorization("MfaComplete")</c>.
    /// </summary>
    public const string MfaComplete = "MfaComplete";
}
