using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class DavetiyeCorsOptionsValidator(bool isProduction) : IValidateOptions<DavetiyeCorsOptions>
{
    public const int MaxAllowedOrigins = 20;

    public ValidateOptionsResult Validate(string? name, DavetiyeCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.AllowedOrigins.Length > MaxAllowedOrigins)
        {
            failures.Add($"Cors:AllowedOrigins must not exceed {MaxAllowedOrigins} entries.");
        }

        if (isProduction && options.AllowedOrigins.Length == 0)
        {
            failures.Add("Cors:AllowedOrigins must configure at least one origin in Production.");
        }

        foreach (var origin in options.AllowedOrigins)
        {
            if (origin == "*")
            {
                failures.Add("Cors:AllowedOrigins must not contain a wildcard origin.");
                continue;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) ||
                (parsedOrigin.Scheme != Uri.UriSchemeHttp && parsedOrigin.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(parsedOrigin.AbsolutePath.TrimEnd('/')) ||
                !string.IsNullOrEmpty(parsedOrigin.Query))
            {
                failures.Add($"Cors:AllowedOrigins entry '{origin}' must be an absolute http(s) origin with no path.");
                continue;
            }

            if (isProduction && parsedOrigin.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"Cors:AllowedOrigins entry '{origin}' must use HTTPS in Production.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
