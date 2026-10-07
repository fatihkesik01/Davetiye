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
        if (!Uri.TryCreate(options.AppShellUrl, UriKind.Absolute, out var shell) ||
            shell.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(shell.UserInfo) ||
            shell.AbsolutePath != "/" || !string.IsNullOrEmpty(shell.Query) || !string.IsNullOrEmpty(shell.Fragment))
            failures.Add("PublicWeb:AppShellUrl must be a trusted absolute http(s) web origin with root path and no credentials, query or fragment.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
