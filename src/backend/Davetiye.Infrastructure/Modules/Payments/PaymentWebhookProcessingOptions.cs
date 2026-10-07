namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class PaymentWebhookProcessingOptions
{
    public const string SectionName = "PaymentWebhookWorker";
    public int BatchSize { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseSeconds { get; set; } = 120;
    public int RetryBaseSeconds { get; set; } = 15;
    public int RetryMaximumSeconds { get; set; } = 3600;
    public int MaximumAttempts { get; set; } = 12;
}
