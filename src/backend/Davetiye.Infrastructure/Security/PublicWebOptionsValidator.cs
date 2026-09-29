using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class PublicWebOptionsValidator(bool isProduction) : IValidateOptions<PublicWebOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            (parsedBaseUrl.Scheme != Uri.UriSchemeHttp && parsedBaseUrl.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("PublicWeb:BaseUrl must be an absolute http(s) URL.");
        }
        else if (isProduction && parsedBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("PublicWeb:BaseUrl must use HTTPS in Production.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
