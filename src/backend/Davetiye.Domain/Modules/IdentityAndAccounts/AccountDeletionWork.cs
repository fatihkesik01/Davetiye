namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// Durable checkpoints for account deletion orchestration. Provider media deletion is tracked by
/// the media module's own outbox and does not delay account tombstoning.
/// </summary>
public sealed class AccountDeletionWork
{
    private AccountDeletionWork() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string Status { get; private set; } = AccountDeletionWorkStatus.Queued;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? SubscriptionCancellationsQueuedAtUtc { get; private set; }
    public DateTimeOffset? InvitationsPurgeQueuedAtUtc { get; private set; }
    public DateTimeOffset? IdentitySanitizedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastErrorKind { get; private set; }
    public DateTimeOffset? LastErrorAtUtc { get; private set; }

    public static AccountDeletionWork Create(Guid id, Guid accountId, DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty || accountId == Guid.Empty)
            throw new ArgumentException("Deletion work and account identifiers must not be empty.");
        EnsureUtc(startedAtUtc, nameof(startedAtUtc));
        return new AccountDeletionWork { Id = id, AccountId = accountId, StartedAtUtc = startedAtUtc };
    }

    public bool MarkSubscriptionCancellationsQueued(DateTimeOffset atUtc) => MarkCheckpoint(
        refValue: SubscriptionCancellationsQueuedAtUtc,
        assign: value => SubscriptionCancellationsQueuedAtUtc = value,
        atUtc);

    public bool MarkInvitationsPurgeQueued(DateTimeOffset atUtc) => MarkCheckpoint(
        refValue: InvitationsPurgeQueuedAtUtc,
        assign: value => InvitationsPurgeQueuedAtUtc = value,
        atUtc);

    public bool MarkIdentitySanitized(DateTimeOffset atUtc) => MarkCheckpoint(
        refValue: IdentitySanitizedAtUtc,
        assign: value => IdentitySanitizedAtUtc = value,
        atUtc);

    public bool RecordRetry(string errorKind, DateTimeOffset failedAtUtc, DateTimeOffset nextAttemptAtUtc)
    {
        EnsureUtc(failedAtUtc, nameof(failedAtUtc));
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (Status == AccountDeletionWorkStatus.Completed)
            return false;
        if (string.IsNullOrWhiteSpace(errorKind) || errorKind.Length > 64 || !errorKind.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            throw new ArgumentException("A bounded error code is required.", nameof(errorKind));
        if (nextAttemptAtUtc <= failedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAtUtc), "Retry must be scheduled after failure.");
        AttemptCount++;
        Status = AccountDeletionWorkStatus.Retrying;
        LastErrorKind = errorKind;
        LastErrorAtUtc = failedAtUtc;
        NextAttemptAtUtc = nextAttemptAtUtc;
        return true;
    }

    public bool Complete(DateTimeOffset completedAtUtc)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        if (Status == AccountDeletionWorkStatus.Completed)
            return false;
        if (SubscriptionCancellationsQueuedAtUtc is null || InvitationsPurgeQueuedAtUtc is null || IdentitySanitizedAtUtc is null)
            throw new InvalidOperationException("Deletion cannot complete before cancellation, purge, and identity checkpoints are durable.");
        Status = AccountDeletionWorkStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        NextAttemptAtUtc = null;
        LastErrorKind = null;
        LastErrorAtUtc = null;
        return true;
    }

    private bool MarkCheckpoint(DateTimeOffset? refValue, Action<DateTimeOffset> assign, DateTimeOffset atUtc)
    {
        EnsureUtc(atUtc, nameof(atUtc));
        if (Status == AccountDeletionWorkStatus.Completed || refValue is not null)
            return false;
        assign(atUtc);
        Status = AccountDeletionWorkStatus.Queued;
        NextAttemptAtUtc = null;
        LastErrorKind = null;
        LastErrorAtUtc = null;
        return true;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}

public static class AccountDeletionWorkStatus
{
    public const string Queued = "Queued";
    public const string Retrying = "Retrying";
    public const string Completed = "Completed";
}
