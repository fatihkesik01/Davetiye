using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

/// <summary>Integration Foundation owns the outbox entity, table, claims, and queue statistics.</summary>
public sealed class OutboxWorkStore(DavetiyeDbContext db) : IOutboxWorkStore
{
    public async Task AppendAsync(OutboxWorkAppend work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Outbox work must be appended inside its producer transaction.");
        if (work.Id == Guid.Empty || string.IsNullOrWhiteSpace(work.MessageType) || work.MessageType.Length > 200 ||
            work.Payload is null || work.CreatedAt.Offset != TimeSpan.Zero || work.OwnerAccountId == Guid.Empty)
            throw new ArgumentException("Outbox work metadata is invalid.", nameof(work));
        var createdAt = NormalizeDatabaseTimestamp(work.CreatedAt);

        // Stable producer IDs make transaction retries idempotent. ON CONFLICT avoids aborting an
        // enclosing PostgreSQL transaction on replay; the persisted opaque message is then checked.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                (id, message_type, payload, created_at, processed_at, attempt_count, next_attempt_at, claimed_until, failed_permanently, revision, owner_account_id)
            VALUES
                ({work.Id}, {work.MessageType.Trim()}, {work.Payload}, {createdAt}, NULL, 0, {createdAt}, NULL, false, 0, {work.OwnerAccountId})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);

        var persisted = await db.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == work.Id, cancellationToken);
        if (persisted.MessageType != work.MessageType.Trim() || persisted.Payload != work.Payload ||
            persisted.CreatedAt != createdAt || persisted.OwnerAccountId != work.OwnerAccountId)
            throw new InvalidOperationException("The stable outbox ID is already occupied by different work.");
    }

    public async Task<bool> ReplacePayloadAsync(string messageType, Guid messageId, string expectedPayload,
        string replacementPayload, CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        if (messageId == Guid.Empty || expectedPayload is null || replacementPayload is null)
            throw new ArgumentException("A message ID and both payload versions are required.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Outbox payload upgrades must run inside the producer transaction.");
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET payload = {replacementPayload}, revision = revision + 1
            WHERE id = {messageId} AND message_type = {messageType} AND payload = {expectedPayload}
              AND processed_at IS NULL
            """, cancellationToken);
        return affected == 1;
    }

    public async Task<OutboxWorkSnapshot?> FindAsync(string messageType, Guid messageId,
        CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        if (messageId == Guid.Empty) throw new ArgumentException("An outbox message ID is required.", nameof(messageId));
        return await db.OutboxMessages.AsNoTracking()
            .Where(message => message.Id == messageId && message.MessageType == messageType)
            .Select(message => new OutboxWorkSnapshot(message.Payload, message.CreatedAt, message.ProcessedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimedOutboxWork>> ClaimAsync(string messageType, int batchSize,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero || now.Offset != TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var claimedUntil = now + leaseDuration;
        var claimed = await db.OutboxMessages.FromSqlInterpolated($"""
            WITH claimable AS (
                SELECT id
                FROM outbox_messages
                WHERE message_type = {messageType}
                  AND processed_at IS NULL
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
            """).AsNoTracking().ToListAsync(cancellationToken);

        return claimed.Select(message => new ClaimedOutboxWork(
            new OutboxClaimReceipt(message.Id, message.ClaimedUntil ?? throw new InvalidOperationException("Claimed work omitted its lease.")),
            message.Payload, message.AttemptCount, message.CreatedAt, message.OwnerAccountId)).ToArray();
    }

    public async Task<OwnedOutboxDispatchOutcome> AuthorizeOwnedDispatchAsync(OutboxClaimReceipt receipt,
        Guid ownerAccountId, bool suppress, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (ownerAccountId == Guid.Empty || receipt.MessageId == Guid.Empty ||
            receipt.LeaseUntil.Offset != TimeSpan.Zero || at.Offset != TimeSpan.Zero || receipt.LeaseUntil <= at)
            throw new ArgumentException("Owner dispatch metadata is invalid.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Owned dispatch authorization requires the account lock transaction.");

        var affected = suppress
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE outbox_messages
                SET processed_at = {at}, claimed_until = NULL, revision = revision + 1
                WHERE id = {receipt.MessageId} AND message_type = {EmailOutboxMessageType}
                  AND owner_account_id = {ownerAccountId} AND claimed_until = {receipt.LeaseUntil}
                  AND claimed_until > {at} AND processed_at IS NULL AND failed_permanently = false
                """, cancellationToken)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE outbox_messages
                SET dispatch_started_at_utc = COALESCE(dispatch_started_at_utc, {at}), revision = revision + 1
                WHERE id = {receipt.MessageId} AND message_type = {EmailOutboxMessageType}
                  AND owner_account_id = {ownerAccountId} AND claimed_until = {receipt.LeaseUntil}
                  AND claimed_until > {at} AND processed_at IS NULL AND failed_permanently = false
                """, cancellationToken);
        if (affected != 1) return OwnedOutboxDispatchOutcome.LeaseLost;
        return suppress ? OwnedOutboxDispatchOutcome.Suppressed : OwnedOutboxDispatchOutcome.Authorized;
    }

    public async Task CompleteAsync(OutboxClaimReceipt receipt, DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt, completedAt);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET processed_at = {completedAt}, claimed_until = NULL, revision = revision + 1
            WHERE id = {receipt.MessageId} AND claimed_until = {receipt.LeaseUntil}
              AND claimed_until > {completedAt} AND processed_at IS NULL AND failed_permanently = false
            """, cancellationToken);
        if (affected != 1) throw new InvalidOperationException("Outbox claim receipt is stale or unavailable.");
    }

    public async Task FailAsync(OutboxClaimReceipt receipt, DateTimeOffset failedAt, DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt, failedAt);
        if (nextAttemptAt is not null && (nextAttemptAt.Value.Offset != TimeSpan.Zero || nextAttemptAt <= failedAt))
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET attempt_count = attempt_count + 1,
                next_attempt_at = COALESCE({nextAttemptAt}::timestamp with time zone, next_attempt_at),
                claimed_until = NULL,
                failed_permanently = CASE WHEN {nextAttemptAt}::timestamp with time zone IS NULL THEN true ELSE false END,
                revision = revision + 1
            WHERE id = {receipt.MessageId} AND claimed_until = {receipt.LeaseUntil}
              AND claimed_until > {failedAt} AND processed_at IS NULL AND failed_permanently = false
            """, cancellationToken);
        if (affected != 1) throw new InvalidOperationException("Outbox claim receipt is stale or unavailable.");
    }

    public async Task<IReadOnlyList<TerminalOutboxWork>> GetTerminalAsync(string messageType, int batchSize,
        Guid? afterMessageId, CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var query = db.OutboxMessages.AsNoTracking()
            .Where(message => message.MessageType == messageType && message.ProcessedAt == null && message.FailedPermanently);
        if (afterMessageId is not null)
            query = query.Where(message => message.Id.CompareTo(afterMessageId.Value) > 0);
        return await query.OrderBy(message => message.Id).Take(batchSize)
            .Select(message => new TerminalOutboxWork(message.Id, message.Payload, message.AttemptCount, message.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ResolveTerminalAsync(string messageType, Guid messageId, DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        ValidateTerminalOperation(messageId, completedAt);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET processed_at = {completedAt}, failed_permanently = false, claimed_until = NULL, revision = revision + 1
            WHERE id = {messageId} AND message_type = {messageType} AND processed_at IS NULL AND failed_permanently = true
            """, cancellationToken);
        return affected == 1;
    }

    public async Task<bool> RetryTerminalAsync(string messageType, Guid messageId, DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        ValidateTerminalOperation(messageId, nextAttemptAt);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages
            SET attempt_count = 0, next_attempt_at = {nextAttemptAt}, claimed_until = NULL,
                failed_permanently = false, revision = revision + 1
            WHERE id = {messageId} AND message_type = {messageType} AND processed_at IS NULL AND failed_permanently = true
            """, cancellationToken);
        return affected == 1;
    }

    public async Task<OutboxQueueStatistics> GetStatisticsAsync(string messageType, CancellationToken cancellationToken)
    {
        ValidateMessageType(messageType);
        var messages = db.OutboxMessages.AsNoTracking().Where(message => message.MessageType == messageType);
        var pending = messages.Where(message => message.ProcessedAt == null && !message.FailedPermanently);
        var pendingCount = await pending.LongCountAsync(cancellationToken);
        var terminalCount = await messages.LongCountAsync(message => message.FailedPermanently, cancellationToken);
        var oldest = pendingCount == 0 ? null : await pending.MinAsync(message => (DateTimeOffset?)message.CreatedAt, cancellationToken);
        return new(pendingCount, terminalCount, oldest);
    }

    private static void ValidateReceipt(OutboxClaimReceipt receipt, DateTimeOffset operationAt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.MessageId == Guid.Empty || receipt.LeaseUntil.Offset != TimeSpan.Zero || operationAt.Offset != TimeSpan.Zero ||
            receipt.LeaseUntil <= operationAt)
            throw new InvalidOperationException("Outbox claim receipt is stale or invalid.");
    }

    private static void ValidateMessageType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType) || messageType.Length > 200)
            throw new ArgumentException("A bounded outbox message type is required.", nameof(messageType));
    }

    private static void ValidateTerminalOperation(Guid messageId, DateTimeOffset operationAt)
    {
        if (messageId == Guid.Empty || operationAt.Offset != TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationAt));
    }

    private static DateTimeOffset NormalizeDatabaseTimestamp(DateTimeOffset timestamp) =>
        timestamp.AddTicks(-(timestamp.Ticks % 10)); // PostgreSQL timestamp with time zone stores microseconds.

    private const string EmailOutboxMessageType = "notifications.email";
}
