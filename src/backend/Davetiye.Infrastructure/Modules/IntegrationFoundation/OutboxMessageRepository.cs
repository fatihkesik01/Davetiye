using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

/// <summary>
/// EF/PostgreSQL implementation of <see cref="IOutboxMessageRepository"/>. See
/// <see cref="InboxMessageRepository"/>'s remarks on <c>ClaimBatchAsync</c> for why the claim query
/// is a single atomic `FOR UPDATE SKIP LOCKED` + `UPDATE ... RETURNING` statement rather than an
/// EF read-then-write round trip.
/// </summary>
public sealed class OutboxMessageRepository(DavetiyeDbContext dbContext) : IOutboxMessageRepository
{
    public async Task AppendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
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

        var claimed = await dbContext.Set<OutboxMessage>()
            .FromSqlInterpolated($"""
                WITH claimable AS (
                    SELECT id
                    FROM outbox_messages
                    WHERE processed_at IS NULL
                      AND failed_permanently = false
                      AND next_attempt_at <= {now}
                      AND (claimed_until IS NULL OR claimed_until < {now})
                    ORDER BY next_attempt_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE outbox_messages AS m
                SET claimed_until = {claimedUntil}
                FROM claimable
                WHERE m.id = claimable.id
                RETURNING m.*
                """)
            .ToListAsync(cancellationToken);

        return claimed;
    }

    public Task MarkProcessedAsync(
        OutboxMessage message, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.MarkProcessed(processedAt);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task RecordFailedAttemptAsync(
        OutboxMessage message,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.RecordFailedAttempt(failedAt, nextAttemptAt);
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
