namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Per docs/adr/0002 and docs/PHASE_0_BASELINE.md §7: Creator vs. Super Admin is a distinct
/// principal/bootstrap concern, not an ASP.NET Core Identity Roles-table concern — there is no Roles
/// table in this schema (see <c>Davetiye.Infrastructure.Persistence.DavetiyeDbContext</c>'s doc
/// comment). "Is this ApplicationUser a Super Admin" is therefore represented as a claim added only by
/// the one-time Admin bootstrap tool (<c>tools/Davetiye.AdminBootstrap</c>), never by a boolean column
/// any unrelated code path with DB write access could flip.
/// </summary>
public static class SuperAdminClaimNames
{
    public const string SuperAdmin = "davetiye:super-admin";

    public const string SuperAdminClaimValue = "true";

    /// <summary>
    /// An OIDC-style Authentication Methods Reference claim, added only to the specific principal
    /// produced by a successful two-factor sign-in completion
    /// (<c>DavetiyeSignInManager.TwoFactorSignInWithAmrClaimAsync</c>) — never present on a plain
    /// password-only sign-in, even for a Super Admin whose MFA enrollment is still pending. This is
    /// the per-session marker the "MfaComplete" authorization policy checks for.
    /// </summary>
    public const string AuthenticationMethodReference = "amr";

    public const string MfaAmrValue = "mfa";
}
