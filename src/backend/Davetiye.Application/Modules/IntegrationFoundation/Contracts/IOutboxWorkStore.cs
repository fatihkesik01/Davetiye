namespace Davetiye.Application.Modules.IntegrationFoundation.Contracts;

/// <summary>Opaque queue-work append request owned by the producer; the outbox owns persistence and dispatch state.</summary>
public sealed record OutboxWorkAppend(
    Guid Id,
    string MessageType,
    string Payload,
    DateTimeOffset CreatedAt,
    Guid? OwnerAccountId = null);

/// <summary>
/// One claimed delivery. Consumers may inspect its opaque payload and attempt count, but must return
/// the receipt unchanged to complete or fail it; database entity identity/state stay inside Integration Foundation.
/// </summary>
public sealed record OutboxClaimReceipt(Guid MessageId, DateTimeOffset LeaseUntil);
public enum OwnedOutboxDispatchOutcome { Authorized, Suppressed, LeaseLost }
public sealed record ClaimedOutboxWork(OutboxClaimReceipt Receipt, string Payload, int AttemptCount, DateTimeOffset CreatedAt,
    Guid? OwnerAccountId = null);
public sealed record TerminalOutboxWork(Guid MessageId, string Payload, int AttemptCount, DateTimeOffset CreatedAt);
public sealed record OutboxWorkSnapshot(string Payload, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);

/// <summary>Aggregate counts and oldest pending timestamp for one exact message type.</summary>
public sealed record OutboxQueueStatistics(long PendingCount, long TerminalCount, DateTimeOffset? OldestPendingCreatedAt);

/// <summary>Provider-neutral typed outbox boundary. All operations scope claims/statistics to one message type.</summary>
public interface IOutboxWorkStore
{
    /// <summary>Idempotently appends by stable ID; a conflicting replay must fail.</summary>
    Task AppendAsync(OutboxWorkAppend work, CancellationToken cancellationToken);

    /// <summary>Replaces one producer-owned payload only if its prior durable value still matches, inside the producer transaction.</summary>
    Task<bool> ReplacePayloadAsync(string messageType, Guid messageId, string expectedPayload, string replacementPayload,
        CancellationToken cancellationToken);

    /// <summary>Reads only the opaque payload and lifecycle timestamps for one producer-owned work item.</summary>
    Task<OutboxWorkSnapshot?> FindAsync(string messageType, Guid messageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimedOutboxWork>> ClaimAsync(string messageType, int batchSize,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Under the caller's account advisory-lock transaction, either marks a current owner message
    /// dispatch-started or completes it as suppressed. A stale claim returns LeaseLost.
    /// </summary>
    Task<OwnedOutboxDispatchOutcome> AuthorizeOwnedDispatchAsync(OutboxClaimReceipt receipt,
        Guid ownerAccountId, bool suppress, DateTimeOffset at, CancellationToken cancellationToken);

    Task CompleteAsync(OutboxClaimReceipt receipt, DateTimeOffset completedAt, CancellationToken cancellationToken);

    Task FailAsync(OutboxClaimReceipt receipt, DateTimeOffset failedAt, DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken);

    /// <summary>Returns a bounded page of terminal work so its owning module can reconcile it.</summary>
    Task<IReadOnlyList<TerminalOutboxWork>> GetTerminalAsync(string messageType, int batchSize, Guid? afterMessageId,
        CancellationToken cancellationToken);

    /// <summary>Marks terminal work as completed after the owning provider confirms it is already absent.</summary>
    Task<bool> ResolveTerminalAsync(string messageType, Guid messageId, DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    /// <summary>Explicitly reopens one terminal item for another idempotent provider attempt.</summary>
    Task<bool> RetryTerminalAsync(string messageType, Guid messageId, DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    Task<OutboxQueueStatistics> GetStatisticsAsync(string messageType, CancellationToken cancellationToken);
}
