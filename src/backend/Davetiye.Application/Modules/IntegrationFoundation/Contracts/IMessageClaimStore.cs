using Davetiye.Domain.Modules.IntegrationFoundation;

namespace Davetiye.Application.Modules.IntegrationFoundation.Contracts;

/// <summary>
/// The generic claim/processing contract shared by inbox and outbox persistence
/// (docs/PHASE_0_BASELINE.md §2: "Integration Foundation... Application'da generic inbox/outbox
/// contract'larını... barındırır"). A worker primitive depends only on this shape — it does not
/// need to know whether <typeparamref name="TMessage"/> is an <see cref="InboxMessage"/>, an
/// <see cref="OutboxMessage"/>, or a future message kind this module has not seen yet.
///
/// Implementations (Infrastructure) must make <see cref="ClaimBatchAsync"/> safe under multiple
/// concurrent workers claiming from the same table: two concurrent calls must never both return the
/// same row.
/// </summary>
public interface IMessageClaimStore<TMessage>
{
    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> unprocessed, unclaimed-or-lease-expired,
    /// not-yet-due-for-retry messages, extending their claim lease to
    /// <paramref name="now"/> + <paramref name="leaseDuration"/>. Safe to call concurrently from
    /// multiple workers/processes against the same underlying store.
    /// </summary>
    Task<IReadOnlyList<TMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Persists that <paramref name="message"/> was successfully processed.</summary>
    Task MarkProcessedAsync(TMessage message, DateTimeOffset processedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Persists that a processing attempt for <paramref name="message"/> failed. Pass
    /// <paramref name="nextAttemptAt"/> as <see langword="null"/> to mark the message permanently
    /// failed.
    /// </summary>
    Task RecordFailedAttemptAsync(
        TMessage message,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken);
}
