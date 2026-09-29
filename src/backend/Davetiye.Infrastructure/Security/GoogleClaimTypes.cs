namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Non-standard Google userinfo claim(s) this app reads beyond the <c>ClaimTypes.*</c> mappings
/// <c>GoogleHandler</c> already wires by default (email, name, given/family name, sub). Mapped
/// explicitly via <c>ClaimActions.MapJsonKey</c> in
/// <see cref="SecurityServiceCollectionExtensions.AddAuthSecurity"/> because the framework's default
/// Google claim actions do not include it.
/// </summary>
public static class GoogleClaimTypes
{
    /// <summary>
    /// Google's boolean "email_verified" userinfo field. docs/THREAT_MODEL.md §6 requires that an
    /// unverified email is never trusted for account creation or matching — see
    /// <c>GoogleSignInService.CompleteSignInAsync</c>.
    /// </summary>
    public const string EmailVerified = "email_verified";
}
