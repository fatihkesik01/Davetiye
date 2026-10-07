using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class EmailOutboxWorkerTests
{
    [Fact]
    public async Task Successful_delivery_completes_claim()
    {
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" }, null));

        var processed = await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(fixture.Store.Completed);
        Assert.Empty(fixture.Store.Failures);
        Assert.Equal("creator@example.test", Assert.Single(fixture.Transport.Messages).ToEmail);
    }

    [Fact]
    public async Task Transient_provider_failure_is_rescheduled_with_exponential_backoff()
    {
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" }, null),
            new EmailDeliveryException("temporary", isTransient: true));

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        var failed = Assert.Single(fixture.Store.Failures);
        Assert.Equal(fixture.Clock.UtcNow, failed.FailedAt);
        Assert.Equal(fixture.Clock.UtcNow.AddSeconds(10), failed.NextAttemptAt);
        Assert.Empty(fixture.Store.Completed);
    }

    [Fact]
    public async Task Permanent_provider_failure_is_dead_lettered()
    {
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" }, null),
            new EmailDeliveryException("permanent", isTransient: false));

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Null(Assert.Single(fixture.Store.Failures).NextAttemptAt);
        Assert.Empty(fixture.Store.Completed);
    }

    [Fact]
    public async Task Expired_auth_envelope_is_dead_lettered_without_delivery()
    {
        var now = DateTimeOffset.Parse("2026-10-06T12:00:00Z");
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.PasswordReset,
            new Dictionary<string, string> { ["resetLink"] = "https://example.test/reset#token" }, now.AddSeconds(-1)));

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Null(Assert.Single(fixture.Store.Failures).NextAttemptAt);
        Assert.Empty(fixture.Transport.Messages);
    }

    [Fact]
    public async Task Corrupt_payload_is_dead_lettered_without_delivery()
    {
        using var fixture = CreateFixture(_ => "v1:corrupt-payload");

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Null(Assert.Single(fixture.Store.Failures).NextAttemptAt);
        Assert.Empty(fixture.Transport.Messages);
    }

    [Fact]
    public async Task Account_owned_pending_email_is_completed_without_delivery_after_deletion_starts()
    {
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" }, null),
            ownerAccountId: Guid.NewGuid(), ownerDeleting: true);

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Single(fixture.Store.Completed);
        Assert.Empty(fixture.Store.Failures);
        Assert.Empty(fixture.Transport.Messages);
    }

    [Fact]
    public async Task Ownerless_legacy_email_is_quarantined_without_delivery()
    {
        using var fixture = CreateFixture(payload => payload.Protect("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" }, null), ownerless: true);

        await fixture.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Null(Assert.Single(fixture.Store.Failures).NextAttemptAt);
        Assert.Empty(fixture.Store.Completed);
        Assert.Empty(fixture.Transport.Messages);
    }

    private static Fixture CreateFixture(Func<EmailOutboxPayloadProtector, string> makePayload,
        EmailDeliveryException? deliveryFailure = null, Guid? ownerAccountId = null, bool ownerDeleting = false,
        bool ownerless = false)
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-10-06T12:00:00Z"));
        var payloadProtector = new EmailOutboxPayloadProtector(new EphemeralDataProtectionProvider());
        var store = new FakeOutboxStore(clock.UtcNow, makePayload(payloadProtector),
            ownerless ? null : ownerAccountId ?? Guid.NewGuid());
        var transport = new FakeTransport(deliveryFailure);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxWorkStore>(store);
        services.AddSingleton<IEmailOwnedDispatchGate>(new FakeEmailOwnedDispatchGate(store, clock, ownerDeleting));
        services.AddSingleton(payloadProtector);
        services.AddSingleton<EmailTemplateRenderer>();
        services.AddSingleton<IEmailTransport>(transport);
        services.AddSingleton<IAccountDeletionStatusReader>(new FakeAccountDeletionStatusReader(ownerDeleting));
        var provider = services.BuildServiceProvider();
        var worker = new EmailOutboxWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailOutboxOptions()), clock, NullLogger<EmailOutboxWorker>.Instance);
        return new Fixture(worker, store, transport, clock, provider);
    }

    private sealed record Fixture(EmailOutboxWorker Worker, FakeOutboxStore Store, FakeTransport Transport,
        FixedClock Clock, ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset initial) : IClock
    {
        public DateTimeOffset UtcNow { get; } = initial;
    }

    private sealed class FakeOutboxStore(DateTimeOffset now, string payload, Guid? ownerAccountId) : IOutboxWorkStore
    {
        private readonly Guid id = Guid.NewGuid();
        public List<DateTimeOffset> Completed { get; } = [];
        public List<(DateTimeOffset FailedAt, DateTimeOffset? NextAttemptAt)> Failures { get; } = [];

        public Task AppendAsync(OutboxWorkAppend work, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ReplacePayloadAsync(string messageType, Guid messageId, string expectedPayload,
            string replacementPayload, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<OutboxWorkSnapshot?> FindAsync(string messageType, Guid messageId,
            CancellationToken cancellationToken) => Task.FromResult<OutboxWorkSnapshot?>(null);

        public Task<IReadOnlyList<ClaimedOutboxWork>> ClaimAsync(string messageType, int batchSize, TimeSpan leaseDuration,
            DateTimeOffset claimNow, CancellationToken cancellationToken)
        {
            if (messageType != EmailOutboxWorker.MessageType || payload.Length == 0)
                return Task.FromResult<IReadOnlyList<ClaimedOutboxWork>>([]);
            var result = new ClaimedOutboxWork(new OutboxClaimReceipt(id, claimNow.Add(leaseDuration)), payload, 0, now, ownerAccountId);
            payload = string.Empty;
            return Task.FromResult<IReadOnlyList<ClaimedOutboxWork>>([result]);
        }

        public Task<OwnedOutboxDispatchOutcome> AuthorizeOwnedDispatchAsync(OutboxClaimReceipt receipt,
            Guid ownerAccountId, bool suppress, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(suppress ? OwnedOutboxDispatchOutcome.Suppressed : OwnedOutboxDispatchOutcome.Authorized);

        public Task CompleteAsync(OutboxClaimReceipt receipt, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            Completed.Add(completedAt);
            return Task.CompletedTask;
        }

        public Task FailAsync(OutboxClaimReceipt receipt, DateTimeOffset failedAt, DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken)
        {
            Failures.Add((failedAt, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TerminalOutboxWork>> GetTerminalAsync(string messageType, int batchSize,
            Guid? afterMessageId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TerminalOutboxWork>>([]);

        public Task<bool> ResolveTerminalAsync(string messageType, Guid messageId, DateTimeOffset completedAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RetryTerminalAsync(string messageType, Guid messageId, DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<OutboxQueueStatistics> GetStatisticsAsync(string messageType, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class FakeEmailOwnedDispatchGate(FakeOutboxStore store, FixedClock clock, bool isDeleting)
        : IEmailOwnedDispatchGate
    {
        public async Task<OwnedOutboxDispatchOutcome> AuthorizeAsync(OutboxClaimReceipt receipt,
            Guid ownerAccountId, DateTimeOffset at, CancellationToken cancellationToken)
        {
            if (!isDeleting) return OwnedOutboxDispatchOutcome.Authorized;
            await store.CompleteAsync(receipt, clock.UtcNow, cancellationToken);
            return OwnedOutboxDispatchOutcome.Suppressed;
        }
    }

    private sealed class FakeAccountDeletionStatusReader(bool isDeleting) : IAccountDeletionStatusReader
    {
        public Task<bool> IsDeletingAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(isDeleting);
    }

    private sealed class FakeTransport(EmailDeliveryException? failure) : IEmailTransport
    {
        public List<EmailDeliveryMessage> Messages { get; } = [];

        public Task SendAsync(EmailDeliveryMessage message, Guid idempotencyId, CancellationToken cancellationToken)
        {
            if (failure is not null)
                throw failure;
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
