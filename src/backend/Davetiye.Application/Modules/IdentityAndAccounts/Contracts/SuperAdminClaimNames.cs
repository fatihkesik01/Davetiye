namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Stable claim identifiers shared by authentication, authorization, and application endpoints.
/// Super Admin identity is granted only by the one-time bootstrap flow and is not an Identity role
/// or a mutable account flag.
/// </summary>
public static class SuperAdminClaimNames
{
    public const string SuperAdmin = "davetiye:super-admin";

    public const string SuperAdminClaimValue = "true";

    /// <summary>Per-session marker added after successful two-factor sign-in completion.</summary>
    public const string AuthenticationMethodReference = "amr";

    public const string MfaAmrValue = "mfa";
}
