using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PaymentWebhookProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Reference = "dv0123456789abcdef0123456789abcdef";
    private const string PaymentId = "123456789";
    private const string Token = "cf311111-2222-4333-8444-555555555555";

    [Fact]
    public async Task Verified_success_is_forwarded_to_atomic_store_with_reconciled_identities()
    {
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(VerifiedResult());

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.Processed, outcome);
        Assert.Equal(PaymentId, verifier.RequestedPaymentId);
        Assert.NotNull(store.Finalized);
        Assert.Equal(PaymentId, store.Finalized.PaymentId);
        Assert.Equal(PaymentId, store.Finalized.VerifiedPaymentId);
        Assert.Equal("SUCCESS", store.Finalized.PaymentStatus);
        Assert.Equal(1, store.Finalized.FraudStatus);
    }

    [Theory]
    [InlineData("PENDING", 0)]
    [InlineData("SUCCESS", 0)]
    public async Task Pending_or_review_result_retries_without_finalizing(string status, int fraudStatus)
    {
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(VerifiedResult(paymentStatus: status, fraudStatus: fraudStatus));

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.RetryScheduled, outcome);
        Assert.Null(store.Finalized);
        Assert.Equal(Now.AddSeconds(15), store.NextAttemptAt);
    }

    [Fact]
    public async Task Provider_outage_retries_and_leaves_payment_attempt_untouched()
    {
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(new(ProviderPaymentVerificationOutcome.Unavailable));

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.RetryScheduled, outcome);
        Assert.Null(store.Finalized);
        Assert.Equal(1, store.FailureRecords);
        Assert.Null(store.MarkedHandled);
    }

    [Fact]
    public async Task Provider_identity_or_amount_mismatch_fails_closed_without_grant_request()
    {
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(VerifiedResult(price: 1m));

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.PermanentlyFailed, outcome);
        Assert.Null(store.Finalized);
        Assert.Null(store.NextAttemptAt);
    }

    [Fact]
    public async Task Provider_response_token_mismatch_fails_closed()
    {
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(VerifiedResult() with { CheckoutToken = "cf399999-2222-4333-8444-555555555555" });

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.PermanentlyFailed, outcome);
        Assert.Null(store.Finalized);
    }

    [Fact]
    public async Task Duplicate_for_already_terminal_attempt_is_marked_handled_without_provider_lookup()
    {
        var store = new FakeStore(Snapshot(status: "Succeeded"));
        var verifier = new FakeVerifier(new(ProviderPaymentVerificationOutcome.Unavailable));

        var outcome = await Processor(store, verifier).ProcessAsync(Work(), CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.AlreadyHandled, outcome);
        Assert.Equal(0, verifier.RequestCount);
        Assert.Equal(Guid.Parse("20000000-0000-0000-0000-000000000001"), store.MarkedHandled);
    }

    [Fact]
    public async Task Callback_event_digest_mismatch_is_permanently_rejected_before_lookup()
    {
        var work = Work() with { ProviderEventId = new string('0', 64) };
        var store = new FakeStore(Snapshot());
        var verifier = new FakeVerifier(VerifiedResult());

        var outcome = await Processor(store, verifier).ProcessAsync(work, CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.PermanentlyFailed, outcome);
        Assert.Equal(0, verifier.RequestCount);
        Assert.Null(store.Finalized);
    }

    private static PaymentWebhookProcessor Processor(FakeStore store, FakeVerifier verifier) => new(
        store, verifier, new FrozenClock(Now), Options.Create(new PaymentWebhookProcessingOptions()));

    private static PaymentWebhookWorkItem Work() => new(
        Guid.Parse("20000000-0000-0000-0000-000000000001"), EventId(),
        $$"""{"schemaVersion":1,"provider":"iyzico","format":"hpp","eventType":"CHECKOUT_FORM_AUTH","paymentId":"{{PaymentId}}","paymentConversationId":"{{Reference}}","status":"SUCCESS"}""",
        0);

    private static string EventId() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        "CHECKOUT_FORM_AUTH" + PaymentId + Token + Reference + "SUCCESS"))).ToLowerInvariant();

    private static PaymentAttemptVerificationSnapshot Snapshot(string status = "Pending") => new(
        Guid.Parse("20000000-0000-0000-0000-000000000002"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "standard", 699m, "TRY", Reference, status, Token, null, null);

    private static ProviderPaymentVerificationResult VerifiedResult(
        string paymentStatus = "SUCCESS", int fraudStatus = 1, decimal price = 699m) => new(
        ProviderPaymentVerificationOutcome.Verified, PaymentId, "TRY", Reference, Reference,
        price, 699m, "success", paymentStatus, fraudStatus, Token);

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeVerifier(ProviderPaymentVerificationResult result) : IPaymentResultVerifier
    {
        public int RequestCount { get; private set; }
        public string? RequestedPaymentId { get; private set; }
        public Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(string paymentId, string checkoutToken,
            string conversationId, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestedPaymentId = paymentId;
            return Task.FromResult(result.Outcome == ProviderPaymentVerificationOutcome.Verified && result.CheckoutToken is null
                ? result with { CheckoutToken = checkoutToken }
                : result);
        }
    }

    private sealed class FakeStore(PaymentAttemptVerificationSnapshot? attempt) : IPaymentWebhookProcessingStore
    {
        public PaymentWebhookFinalizationRequest? Finalized { get; private set; }
        public Guid? MarkedHandled { get; private set; }
        public int FailureRecords { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }

        public Task<IReadOnlyList<PaymentWebhookWorkItem>> ClaimAsync(string providerName, int batchSize,
            TimeSpan leaseDuration, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentWebhookWorkItem>>([]);

        public Task<PaymentAttemptVerificationSnapshot?> FindAttemptAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(attempt?.Reference == reference ? attempt : null);

        public Task<PaymentWebhookStoreOutcome> FinalizeAsync(PaymentWebhookFinalizationRequest request, CancellationToken cancellationToken)
        {
            Finalized = request;
            return Task.FromResult(PaymentWebhookStoreOutcome.Processed);
        }

        public Task<PaymentWebhookStoreOutcome> MarkHandledAsync(Guid messageId, DateTimeOffset processedAtUtc, CancellationToken cancellationToken)
        {
            MarkedHandled = messageId;
            return Task.FromResult(PaymentWebhookStoreOutcome.AlreadyHandled);
        }

        public Task<PaymentWebhookStoreOutcome> RecordFailureAsync(Guid messageId, DateTimeOffset failedAtUtc,
            DateTimeOffset? nextAttemptAtUtc, CancellationToken cancellationToken)
        {
            FailureRecords++;
            NextAttemptAt = nextAttemptAtUtc;
            return Task.FromResult(nextAttemptAtUtc is null
                ? PaymentWebhookStoreOutcome.PermanentlyFailed
                : PaymentWebhookStoreOutcome.RetryScheduled);
        }

        public Task<PaymentReversalStoreOutcome> ApplyVerifiedReversalAsync(string providerPaymentId,
            VerifiedPaymentReversalOutcome outcome, DateTimeOffset confirmedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(PaymentReversalStoreOutcome.NoAccessChange);
    }
}
