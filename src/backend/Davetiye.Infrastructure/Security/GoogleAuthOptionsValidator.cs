using System.Net;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Enforces docs/THREAT_MODEL.md §6/§12 gate 3 ("Google config fail-closed"): when
/// <see cref="GoogleAuthOptions.Enabled"/> is false (the default), every other field is unchecked —
/// Google sign-in simply stays off. Once an operator explicitly enables it, this validator requires a
/// real client id/secret and, in Production specifically, a verified HTTPS callback host (never a raw
/// IP or "localhost" — docs/ARCHITECTURE.md §2). A misconfigured-but-enabled Production setting fails
/// host startup outright rather than silently falling back to "disabled".
/// </summary>
internal sealed class GoogleAuthOptionsValidator(bool isProduction) : IValidateOptions<GoogleAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("GoogleAuth:ClientId is required when GoogleAuth:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("GoogleAuth:ClientSecret is required when GoogleAuth:Enabled is true.");
        }

        if (!Uri.TryCreate(options.CallbackBaseUrl, UriKind.Absolute, out var callbackUri) ||
            (callbackUri.Scheme != Uri.UriSchemeHttp && callbackUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                "GoogleAuth:CallbackBaseUrl must be an absolute http(s) URL when GoogleAuth:Enabled is true.");
        }
        else if (isProduction)
        {
            if (callbackUri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add("GoogleAuth:CallbackBaseUrl must use HTTPS in Production.");
            }

            // Google will not accept a raw IP, and this platform does not treat "localhost" as a
            // verified production domain, so both fail the domain+HTTPS prerequisite this gate exists
            // to enforce (docs/ARCHITECTURE.md §2, docs/PRODUCT.md §3).
            if (IPAddress.TryParse(callbackUri.Host, out _) ||
                string.Equals(callbackUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    "GoogleAuth:CallbackBaseUrl must use a verified domain (not a raw IP or localhost) in Production.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
