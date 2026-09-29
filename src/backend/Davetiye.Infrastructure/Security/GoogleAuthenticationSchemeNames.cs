namespace Davetiye.Infrastructure.Security;

/// <summary>
/// The authentication scheme name Google sign-in is registered under (equal in value to
/// <c>Microsoft.AspNetCore.Authentication.Google.GoogleDefaults.AuthenticationScheme</c>, restated
/// here as a plain string constant so Davetiye.Api's composition root can pass it into endpoint
/// mapping without adding a package reference to the Google auth library itself, matching the
/// existing <see cref="AuthRateLimitPolicyNames"/>/<see cref="CorsPolicyNames"/> convention).
/// </summary>
public static class GoogleAuthenticationSchemeNames
{
    public const string Google = "Google";
}
