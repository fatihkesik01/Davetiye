namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>Runtime credentials for authenticated iyzico server-to-server payment retrieval.</summary>
public sealed class IyzicoPaymentApiOptions
{
    public const string SectionName = "Payments:Iyzico";
    public const string DefaultApiBaseUrl = "https://api.iyzipay.com";

    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        IsTrustedBaseUrl(ApiBaseUrl);

    public static bool IsTrustedBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps &&
               uri.IdnHost is "api.iyzipay.com" or "sandbox-api.iyzipay.com";
    }
}
