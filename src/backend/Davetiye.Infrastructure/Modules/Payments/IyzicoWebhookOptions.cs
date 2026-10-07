namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class IyzicoWebhookOptions
{
    public const string SectionName = "Payments:Iyzico";

    public string? WebhookSecretKey { get; set; }

    public string? MerchantId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(WebhookSecretKey) &&
        !string.IsNullOrWhiteSpace(MerchantId) &&
        MerchantId.All(char.IsAsciiDigit);
}
