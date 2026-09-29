using Davetiye.Domain.Modules.IntegrationFoundation;
using Xunit;

namespace Davetiye.UnitTests;

/// <summary>
/// Pure Domain tests (no database) for <see cref="InboxMessage"/> and <see cref="OutboxMessage"/>'s
/// invariant guard clauses and retry/backoff/terminal-failure state-transition logic — the second
/// half of Phase 1 task 9's "unique/idempotent claim ve retry-safe worker testi" acceptance
/// criterion (the first half, DB-level uniqueness, is proven against real PostgreSQL in
/// Davetiye.IntegrationTests).
/// </summary>
public sealed class IntegrationFoundationEntityTests
{
    [Fact]
    public void InboxMessage_Create_rejects_a_blank_provider_name()
    {
        Assert.Throws<ArgumentException>(() => InboxMessage.Create(
            Guid.NewGuid(), "   ", "evt-1", "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InboxMessage_Create_rejects_a_blank_provider_event_id()
    {
        Assert.Throws<ArgumentException>(() => InboxMessage.Create(
            Guid.NewGuid(), "iyzico", "  ", "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InboxMessage_Create_rejects_a_null_payload()
    {
        Assert.Throws<ArgumentNullException>(() => InboxMessage.Create(
            Guid.NewGuid(), "iyzico", "evt-1", null!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InboxMessage_Create_builds_a_pending_message_due_immediately()
    {
        var receivedAt = DateTimeOffset.UtcNow;

        var message = InboxMessage.Create(Guid.NewGuid(), " iyzico ", " evt-1 ", "{}", receivedAt);

        Assert.Equal("iyzico", message.ProviderName);
        Assert.Equal("evt-1", message.ProviderEventId);
        Assert.Equal(receivedAt, message.NextAttemptAt);
        Assert.Equal(0, message.AttemptCount);
        Assert.False(message.FailedPermanently);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.ClaimedUntil);
    }

    [Fact]
    public void InboxMessage_MarkProcessed_cannot_be_called_twice()
    {
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", DateTimeOffset.UtcNow);

        message.MarkProcessed(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => message.MarkProcessed(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InboxMessage_RecordFailedAttempt_after_processed_is_rejected()
    {
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", DateTimeOffset.UtcNow);
        message.MarkProcessed(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => message.RecordFailedAttempt(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void InboxMessage_RecordFailedAttempt_with_a_next_attempt_time_reschedules_and_releases_the_claim()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        var nextAttemptAt = now.AddMinutes(5);

        message.RecordFailedAttempt(now, nextAttemptAt);

        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(nextAttemptAt, message.NextAttemptAt);
        Assert.False(message.FailedPermanently);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.ClaimedUntil);
    }

    [Fact]
    public void InboxMessage_RecordFailedAttempt_with_null_next_attempt_marks_permanent_failure()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);

        message.RecordFailedAttempt(now, null);

        Assert.True(message.FailedPermanently);
        Assert.Equal(1, message.AttemptCount);
    }

    [Fact]
    public void InboxMessage_RecordFailedAttempt_rejects_a_next_attempt_at_or_before_the_failure_time()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);

        Assert.Throws<ArgumentOutOfRangeException>(() => message.RecordFailedAttempt(now, now));
    }

    [Fact]
    public void InboxMessage_RecordFailedAttempt_after_permanent_failure_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        message.RecordFailedAttempt(now, null);

        Assert.Throws<InvalidOperationException>(() => message.RecordFailedAttempt(now, now.AddMinutes(1)));
    }

    [Fact]
    public void InboxMessage_MarkProcessed_after_permanent_failure_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        message.RecordFailedAttempt(now, null);

        Assert.Throws<InvalidOperationException>(() => message.MarkProcessed(now));
    }

    [Fact]
    public void OutboxMessage_Create_rejects_a_blank_message_type()
    {
        Assert.Throws<ArgumentException>(() => OutboxMessage.Create(
            Guid.NewGuid(), " ", "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OutboxMessage_Create_builds_a_pending_message_due_immediately()
    {
        var createdAt = DateTimeOffset.UtcNow;

        var message = OutboxMessage.Create(Guid.NewGuid(), " email.rsvp-confirmation ", "{}", createdAt);

        Assert.Equal("email.rsvp-confirmation", message.MessageType);
        Assert.Equal(createdAt, message.NextAttemptAt);
        Assert.Equal(0, message.AttemptCount);
        Assert.False(message.FailedPermanently);
    }

    [Fact]
    public void OutboxMessage_RecordFailedAttempt_with_a_next_attempt_time_reschedules()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create(Guid.NewGuid(), "email.rsvp-confirmation", "{}", now);
        var nextAttemptAt = now.AddSeconds(30);

        message.RecordFailedAttempt(now, nextAttemptAt);

        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(nextAttemptAt, message.NextAttemptAt);
        Assert.False(message.FailedPermanently);
    }

    [Fact]
    public void OutboxMessage_RecordFailedAttempt_with_null_next_attempt_marks_permanent_failure()
    {
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create(Guid.NewGuid(), "email.rsvp-confirmation", "{}", now);

        message.RecordFailedAttempt(now, null);

        Assert.True(message.FailedPermanently);
    }

    [Fact]
    public void OutboxMessage_MarkProcessed_cannot_be_called_twice()
    {
        var message = OutboxMessage.Create(
            Guid.NewGuid(), "email.rsvp-confirmation", "{}", DateTimeOffset.UtcNow);
        message.MarkProcessed(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => message.MarkProcessed(DateTimeOffset.UtcNow));
    }
}
