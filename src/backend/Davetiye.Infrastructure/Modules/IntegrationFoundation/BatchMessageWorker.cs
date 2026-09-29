using Davetiye.Application.Modules.IntegrationFoundation.Contracts;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

/// <summary>
/// Outcome of one <see cref="BatchMessageWorker{TMessage}.ProcessBatchAsync"/> call.
/// </summary>
public sealed record BatchProcessingResult(
    int ClaimedCount,
    int SucceededCount,
    int RescheduledCount,
    int PermanentlyFailedCount);

/// <summary>
/// A minimal, pluggable batch processor generic over any message kind that implements
/// <see cref="IMessageClaimStore{TMessage}"/> (currently inbox and outbox; nothing prevents a future
/// message kind reusing it). This is deliberately the reusable *mechanism* only:
///
/// - It claims a batch from the store (the store owns the concurrency-safe claim SQL).
/// - For each claimed message it invokes a caller-supplied handler delegate.
/// - On success it marks the message processed; on failure it asks the caller for the next attempt
///   time (or <see langword="null"/> for "give up") and records that.
///
/// It intentionally does not know about any real provider, does not decode any payload, and is not
/// itself an <c>IHostedService</c> — a later milestone (Payments, Media, Notifications) supplies the
/// real handler and the real background service that calls <see cref="ProcessBatchAsync"/> on a
/// timer. Production code must not register a fake/test handler here; only test code may do that.
/// </summary>
public sealed class BatchMessageWorker<TMessage>(IMessageClaimStore<TMessage> store)
{
    public async Task<BatchProcessingResult> ProcessBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        Func<TMessage, CancellationToken, Task<bool>> handleAsync,
        Func<TMessage, DateTimeOffset?> computeNextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleAsync);
        ArgumentNullException.ThrowIfNull(computeNextAttemptAt);

        var claimed = await store.ClaimBatchAsync(batchSize, leaseDuration, now, cancellationToken);

        var succeeded = 0;
        var rescheduled = 0;
        var permanentlyFailed = 0;

        foreach (var message in claimed)
        {
            bool handlerSucceeded;
            try
            {
                handlerSucceeded = await handleAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cooperative shutdown of this batch's own token, not a handler failure: let it
                // propagate so the caller (e.g. a hosted service stopping) sees cancellation
                // instead of this message silently consuming a retry/backoff slot it was never
                // actually given a chance to succeed or fail on.
                throw;
            }
            catch
            {
                // Any other handler exception is treated the same as a handler returning false:
                // this one message's failure must not abort the rest of the batch (retry-safety),
                // and must not leave the message permanently claimed with no path back to being
                // retried.
                handlerSucceeded = false;
            }

            if (handlerSucceeded)
            {
                await store.MarkProcessedAsync(message, now, cancellationToken);
                succeeded++;
                continue;
            }

            var nextAttemptAt = computeNextAttemptAt(message);
            await store.RecordFailedAttemptAsync(message, now, nextAttemptAt, cancellationToken);

            if (nextAttemptAt is null)
            {
                permanentlyFailed++;
            }
            else
            {
                rescheduled++;
            }
        }

        return new BatchProcessingResult(claimed.Count, succeeded, rescheduled, permanentlyFailed);
    }
}
