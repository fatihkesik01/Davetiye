using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

/// <summary>
/// EF/PostgreSQL implementation of <see cref="IInboxMessageRepository"/>.
///
/// <see cref="ClaimBatchAsync"/> is the concurrency-safety-critical operation: it must never let two
/// concurrent callers (different workers, different processes, even different concurrent requests
/// against the same process) claim the same row. It deliberately does NOT read rows with EF then
/// write them back (an EF change-tracker read-then-SaveChanges round trip is not atomic across two
/// statements and is not safe under concurrent callers). Instead it issues a single atomic
/// SQL statement: a `FOR UPDATE SKIP LOCKED` candidate selection composed into an
/// `UPDATE ... FROM ... RETURNING` — the row lock PostgreSQL takes for the `UPDATE` (and the
/// `SKIP LOCKED` candidate selection) is what guarantees no two concurrent claims can return the
/// same row; the guarantee comes from PostgreSQL's row locking, not from any in-memory lock in this
/// process, so it is safe across multiple process instances (matching the docs/ARCHITECTURE.md
/// modular-monolith deployment, which can still run more than one background worker instance).
/// </summary>
public sealed class InboxMessageRepository(DavetiyeDbContext dbContext) : IInboxMessageRepository
{
    public async Task AppendAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        dbContext.Set<InboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration), leaseDuration, "Lease duration must be positive.");
        }

        var claimedUntil = now + leaseDuration;

        var claimed = await dbContext.Set<InboxMessage>()
            .FromSqlInterpolated($"""
                WITH claimable AS (
                    SELECT id
                    FROM inbox_messages
                    WHERE processed_at IS NULL
                      AND failed_permanently = false
                      AND next_attempt_at <= {now}
                      AND (claimed_until IS NULL OR claimed_until < {now})
                    ORDER BY next_attempt_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE inbox_messages AS m
                SET claimed_until = {claimedUntil}
                FROM claimable
                WHERE m.id = claimable.id
                RETURNING m.*
                """)
            .ToListAsync(cancellationToken);

        return claimed;
    }

    public Task MarkProcessedAsync(
        InboxMessage message, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.MarkProcessed(processedAt);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task RecordFailedAttemptAsync(
        InboxMessage message,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.RecordFailedAttempt(failedAt, nextAttemptAt);
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
