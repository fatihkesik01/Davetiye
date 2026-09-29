using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Xunit;

namespace Davetiye.UnitTests;

/// <summary>
/// Pure orchestration tests (no database) for <see cref="BatchMessageWorker{TMessage}"/>, using a
/// test-only in-memory fake <see cref="IMessageClaimStore{TMessage}"/> — never a real handler or a
/// real hosted service, per this milestone's scope. Proves the worker is retry-safe: success marks
/// processed, failure reschedules or permanently fails as the caller's backoff function decides, and
/// one message's handler exception never aborts the rest of the batch.
/// </summary>
public sealed class BatchMessageWorkerTests
{
    [Fact]
    public async Task ProcessBatchAsync_marks_every_successfully_handled_message_processed()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FakeMessageClaimStore(
        [
            InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now),
            InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-2", "{}", now)
        ]);
        var worker = new BatchMessageWorker<InboxMessage>(store);

        var result = await worker.ProcessBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(1),
            now: now,
            handleAsync: (_, _) => Task.FromResult(true),
            computeNextAttemptAt: _ => throw new InvalidOperationException("Must not be called on success."));

        Assert.Equal(2, result.ClaimedCount);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(0, result.RescheduledCount);
        Assert.Equal(0, result.PermanentlyFailedCount);
        Assert.Equal(2, store.ProcessedMessageIds.Count);
        Assert.Empty(store.FailedAttempts);
    }

    [Fact]
    public async Task ProcessBatchAsync_reschedules_a_failed_message_using_the_caller_backoff_function()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        var store = new FakeMessageClaimStore([message]);
        var worker = new BatchMessageWorker<InboxMessage>(store);
        var expectedNextAttempt = now.AddMinutes(5);

        var result = await worker.ProcessBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(1),
            now: now,
            handleAsync: (_, _) => Task.FromResult(false),
            computeNextAttemptAt: _ => expectedNextAttempt);

        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(1, result.RescheduledCount);
        Assert.Equal(0, result.PermanentlyFailedCount);
        Assert.Single(store.FailedAttempts);
        Assert.Equal(expectedNextAttempt, store.FailedAttempts[0].NextAttemptAt);
        Assert.Empty(store.ProcessedMessageIds);
    }

    [Fact]
    public async Task ProcessBatchAsync_marks_permanent_failure_when_the_backoff_function_returns_null()
    {
        var now = DateTimeOffset.UtcNow;
        var message = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        var store = new FakeMessageClaimStore([message]);
        var worker = new BatchMessageWorker<InboxMessage>(store);

        var result = await worker.ProcessBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(1),
            now: now,
            handleAsync: (_, _) => Task.FromResult(false),
            computeNextAttemptAt: _ => null);

        Assert.Equal(1, result.PermanentlyFailedCount);
        Assert.Equal(0, result.RescheduledCount);
        Assert.Null(store.FailedAttempts[0].NextAttemptAt);
    }

    [Fact]
    public async Task ProcessBatchAsync_treats_a_handler_exception_as_a_failure_without_aborting_the_batch()
    {
        var now = DateTimeOffset.UtcNow;
        var throwingMessage = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-1", "{}", now);
        var okMessage = InboxMessage.Create(Guid.NewGuid(), "iyzico", "evt-2", "{}", now);
        var store = new FakeMessageClaimStore([throwingMessage, okMessage]);
        var worker = new BatchMessageWorker<InboxMessage>(store);

        var result = await worker.ProcessBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(1),
            now: now,
            handleAsync: (message, _) => message.Id == throwingMessage.Id
                ? throw new InvalidOperationException("Simulated handler failure.")
                : Task.FromResult(true),
            computeNextAttemptAt: _ => now.AddMinutes(1));

        Assert.Equal(2, result.ClaimedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.RescheduledCount);
        Assert.Contains(okMessage.Id, store.ProcessedMessageIds);
        Assert.Contains(throwingMessage.Id, store.FailedAttempts.Select(attempt => attempt.MessageId));
    }

    [Fact]
    public async Task ProcessBatchAsync_with_no_claimable_messages_does_nothing()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FakeMessageClaimStore([]);
        var worker = new BatchMessageWorker<InboxMessage>(store);
        var handlerCalled = false;

        var result = await worker.ProcessBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(1),
            now: now,
            handleAsync: (_, _) => { handlerCalled = true; return Task.FromResult(true); },
            computeNextAttemptAt: _ => now.AddMinutes(1));

        Assert.Equal(0, result.ClaimedCount);
        Assert.False(handlerCalled);
    }

    private sealed class FakeMessageClaimStore(IReadOnlyList<InboxMessage> claimable) : IMessageClaimStore<InboxMessage>
    {
        public List<Guid> ProcessedMessageIds { get; } = [];

        public List<(Guid MessageId, DateTimeOffset? NextAttemptAt)> FailedAttempts { get; } = [];

        public Task<IReadOnlyList<InboxMessage>> ClaimBatchAsync(
            int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(claimable);

        public Task MarkProcessedAsync(
            InboxMessage message, DateTimeOffset processedAt, CancellationToken cancellationToken)
        {
            ProcessedMessageIds.Add(message.Id);
            return Task.CompletedTask;
        }

        public Task RecordFailedAttemptAsync(
            InboxMessage message,
            DateTimeOffset failedAt,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken)
        {
            FailedAttempts.Add((message.Id, nextAttemptAt));
            return Task.CompletedTask;
        }
    }
}
