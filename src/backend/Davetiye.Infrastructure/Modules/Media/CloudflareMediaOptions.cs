using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class CloudflareMediaOptions
{
    public const string SectionName = "CloudflareMedia";

    public bool Enabled { get; init; }
    public string WorkerBaseUrl { get; init; } = string.Empty;
    public string ImageCapabilitySigningKeyBase64Url { get; init; } = string.Empty;
    public string WorkerBrokerAuthorizationKey { get; init; } = string.Empty;
    public string StreamWebhookSecret { get; init; } = string.Empty;
    public int WebhookMaxAgeSeconds { get; init; } = 300;
    public int MaximumVideoPlaybackSessionSeconds { get; init; } = 1800;
    public string StreamCustomerHostname { get; init; } = string.Empty;
}

public sealed class CloudflareMediaOptionsValidator : IValidateOptions<CloudflareMediaOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudflareMediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!Uri.TryCreate(options.WorkerBaseUrl, UriKind.Absolute, out var workerUri) ||
            workerUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(workerUri.Query) ||
            !string.IsNullOrEmpty(workerUri.Fragment) || !string.IsNullOrEmpty(workerUri.UserInfo))
        {
            failures.Add("WorkerBaseUrl must be an absolute HTTPS URL without query, fragment, or user information.");
        }

        if (!HasAtLeast256Bits(options.ImageCapabilitySigningKeyBase64Url))
        {
            failures.Add("ImageCapabilitySigningKeyBase64Url must contain at least 256 bits of base64url-encoded key material.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkerBrokerAuthorizationKey) ||
            System.Text.Encoding.UTF8.GetByteCount(options.WorkerBrokerAuthorizationKey) < 32)
        {
            failures.Add("WorkerBrokerAuthorizationKey must contain at least 256 bits of secret material.");
        }

        if (string.IsNullOrWhiteSpace(options.StreamWebhookSecret) ||
            System.Text.Encoding.UTF8.GetByteCount(options.StreamWebhookSecret) < 16)
        {
            failures.Add("StreamWebhookSecret must contain at least 128 bits of secret material.");
        }

        if (options.WebhookMaxAgeSeconds is < 30 or > 3600)
        {
            failures.Add("WebhookMaxAgeSeconds must be between 30 and 3600 seconds.");
        }
        if (options.MaximumVideoPlaybackSessionSeconds is < 60 or > 3600)
            failures.Add("MaximumVideoPlaybackSessionSeconds must be between 60 and 3600.");
        if (string.IsNullOrWhiteSpace(options.StreamCustomerHostname) || options.StreamCustomerHostname.Length > 253 ||
            options.StreamCustomerHostname.Contains('/') || options.StreamCustomerHostname.Contains('@') ||
            !Uri.TryCreate("https://" + options.StreamCustomerHostname, UriKind.Absolute, out var streamUri) ||
            streamUri.Host != options.StreamCustomerHostname || streamUri.Host.EndsWith(".cloudflarestream.com", StringComparison.OrdinalIgnoreCase) == false ||
            streamUri.AbsolutePath != "/")
            failures.Add("StreamCustomerHostname must be a customer *.cloudflarestream.com hostname.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasAtLeast256Bits(string value)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            return Convert.FromBase64String(base64).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
