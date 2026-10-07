namespace Davetiye.Application.Modules.Payments.Contracts;

public interface IPaymentWebhookIngestor
{
    Task<PaymentWebhookIngestionOutcome> IngestAsync(
        ReadOnlyMemory<byte> body,
        string? signature,
        CancellationToken cancellationToken);
}

public enum PaymentWebhookIngestionOutcome
{
    Accepted,
    Duplicate,
    InvalidPayload,
    InvalidSignature,
    Unavailable
}
