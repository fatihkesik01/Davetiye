namespace Davetiye.Application.Modules.Payments.Contracts;

/// <summary>Minimized normalized provider event claimed by the payment-specific inbox worker.</summary>
public sealed record PaymentWebhookWorkItem(
    Guid MessageId,
    string ProviderEventId,
    string Payload,
    int AttemptCount);

/// <summary>Only server-owned checkout data; no browser or webhook token is included.</summary>
public sealed record PaymentAttemptVerificationSnapshot(
    Guid AttemptId,
    Guid AccountId,
    Guid InvitationId,
    Guid PlanId,
    string PlanKey,
    decimal Amount,
    string Currency,
    string Reference,
    string Status,
    string? ProviderCheckoutId,
    string? ProviderPaymentId,
    Guid? GrantedPlanGrantId);

public sealed record PaymentWebhookFinalizationRequest(
    Guid MessageId,
    string ProviderEventId,
    string EventType,
    string PaymentId,
    string VerifiedPaymentId,
    string PaymentConversationId,
    string HppStatus,
    string Currency,
    string BasketId,
    string ConversationId,
    decimal Price,
    decimal PaidPrice,
    string ProviderResponseStatus,
    string PaymentStatus,
    int FraudStatus,
    DateTimeOffset ProcessedAtUtc);

public enum PaymentWebhookStoreOutcome
{
    Processed,
    AlreadyHandled,
    PermanentlyFailed,
    RetryScheduled
}

/// <summary>Terminal payment outcomes supplied only by a separately authenticated source.</summary>
public enum VerifiedPaymentReversalOutcome
{
    OpenDispute,
    FinalWonChargeback,
    FullRefund,
    FinalLostChargeback
}

public enum PaymentReversalStoreOutcome
{
    Revoked,
    AlreadyRevoked,
    ConflictingOutcome,
    AlreadyResolved,
    NoAccessChange,
    /// <summary>Retryable: the trusted terminal reversal arrived before its payment was settled.</summary>
    NotFound
}

/// <summary>
/// Persistence transaction port for payment inbox work. Implementations claim only the requested
/// provider and atomically finalize payment attempt, publication grant, purchase email, and inbox row.
/// </summary>
public interface IPaymentWebhookProcessingStore
{
    Task<IReadOnlyList<PaymentWebhookWorkItem>> ClaimAsync(
        string providerName,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<PaymentAttemptVerificationSnapshot?> FindAttemptAsync(
        string reference,
        CancellationToken cancellationToken);

    Task<PaymentWebhookStoreOutcome> FinalizeAsync(
        PaymentWebhookFinalizationRequest request,
        CancellationToken cancellationToken);

    Task<PaymentWebhookStoreOutcome> MarkHandledAsync(
        Guid messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentWebhookStoreOutcome> RecordFailureAsync(
        Guid messageId,
        DateTimeOffset failedAtUtc,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a provider-neutral, already-authenticated terminal reversal. This port is not exposed
    /// as an API and must only be called after a trusted source verifies the terminal outcome. The
    /// timestamp is the local UTC application instant from IClock, never the unsigned provider event time.
    /// </summary>
    Task<PaymentReversalStoreOutcome> ApplyVerifiedReversalAsync(
        string providerPaymentId,
        VerifiedPaymentReversalOutcome outcome,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);
}
