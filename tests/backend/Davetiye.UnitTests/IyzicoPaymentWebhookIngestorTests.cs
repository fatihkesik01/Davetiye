using System.Text;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class IyzicoPaymentWebhookIngestorTests
{
    private const string KnownHppBody = """
        {"paymentConversationId":"dv0123456789abcdef0123456789abcdef","merchantId":3404590,"status":"SUCCESS","token":"00000000-0000-4000-8000-000000000001","iyziReferenceCode":"00000000-0000-4000-8000-000000000002","iyziEventType":"CHECKOUT_FORM_AUTH","iyziEventTime":1766733201159,"iyziPaymentId":123}
        """;
    private const string KnownHppSignature = "ffda1fb8df0e66e7fd90ccf39452713847a7ad9fd115863596d1843d141f7120";
    private const string ExpectedEventId = "2d746e72dae1c8dbc25f4865d1748ffcae833efe3b6823185ac818c059651e22";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OfficialHppV3VectorIsVerifiedAndOnlyNormalizedFieldsArePersisted()
    {
        var inbox = new FakeInboxWriter();
        var ingestor = CreateIngestor(inbox);

        var outcome = await ingestor.IngestAsync(Encoding.UTF8.GetBytes(KnownHppBody), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.Accepted, outcome);
        var saved = Assert.Single(inbox.Messages);
        Assert.Equal("iyzico-hpp", saved.ProviderName);
        Assert.Equal(ExpectedEventId, saved.ProviderEventId);
        Assert.Equal(Now, saved.ReceivedAt);
        Assert.Contains("\"eventType\":\"CHECKOUT_FORM_AUTH\"", saved.Payload);
        Assert.Contains("\"paymentId\":\"123\"", saved.Payload);
        Assert.Contains("\"paymentConversationId\":\"dv0123456789abcdef0123456789abcdef\"", saved.Payload);
        Assert.Contains("\"status\":\"SUCCESS\"", saved.Payload);
        Assert.Contains("\"untrustedProviderReferenceCode\":\"00000000-0000-4000-8000-000000000002\"", saved.Payload);
        Assert.Contains("\"untrustedProviderEventTimeUnixMs\":1766733201159", saved.Payload);
        Assert.DoesNotContain("token", saved.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("merchantId", saved.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSignatureIsRejectedWithoutInboxWrite()
    {
        var inbox = new FakeInboxWriter();
        var result = await CreateIngestor(inbox).IngestAsync(
            Encoding.UTF8.GetBytes(KnownHppBody), new string('0', 64), default);

        Assert.Equal(PaymentWebhookIngestionOutcome.InvalidSignature, result);
        Assert.Empty(inbox.Messages);
    }

    [Fact]
    public async Task MissingRuntimeSecretFailsClosedWithoutInboxWrite()
    {
        var inbox = new FakeInboxWriter();
        var ingestor = new IyzicoPaymentWebhookIngestor(
            Options.Create(new IyzicoWebhookOptions { MerchantId = "3404590" }), inbox, new FixedClock());

        var result = await ingestor.IngestAsync(Encoding.UTF8.GetBytes(KnownHppBody), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.Unavailable, result);
        Assert.Empty(inbox.Messages);
    }

    [Fact]
    public async Task DuplicateProviderEventIsAcknowledgedOnlyAfterUniqueConstraintCollision()
    {
        var inbox = new FakeInboxWriter { Duplicate = true };

        var result = await CreateIngestor(inbox).IngestAsync(
            Encoding.UTF8.GetBytes(KnownHppBody), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.Duplicate, result);
        Assert.Single(inbox.Messages);
    }

    [Fact]
    public async Task UnsignedReferenceAndTimestampCannotChangeTheSignedEventIdentity()
    {
        var inbox = new FakeInboxWriter { Duplicate = true };
        var body = KnownHppBody
            .Replace("00000000-0000-4000-8000-000000000002", "00000000-0000-4000-8000-000000000003", StringComparison.Ordinal)
            .Replace("1766733201159", "1766733202159", StringComparison.Ordinal);

        var result = await CreateIngestor(inbox).IngestAsync(Encoding.UTF8.GetBytes(body), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.Duplicate, result);
        var saved = Assert.Single(inbox.Messages);
        Assert.Equal(ExpectedEventId, saved.ProviderEventId);
        Assert.Contains("untrustedProviderReferenceCode", saved.Payload, StringComparison.Ordinal);
        Assert.Contains("untrustedProviderEventTimeUnixMs", saved.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonDuplicateStorageFailureIsNotAcknowledged()
    {
        var inbox = new FakeInboxWriter { ThrowStorageFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateIngestor(inbox).IngestAsync(
            Encoding.UTF8.GetBytes(KnownHppBody), KnownHppSignature, default));
    }

    [Fact]
    public async Task UndocumentedEventTypeOrStatusIsNotAccepted()
    {
        var inbox = new FakeInboxWriter();
        var body = KnownHppBody.Replace("CHECKOUT_FORM_AUTH", "CHARGEBACK_LOST", StringComparison.Ordinal);
        var result = await CreateIngestor(inbox).IngestAsync(Encoding.UTF8.GetBytes(body), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.InvalidPayload, result);
        Assert.Empty(inbox.Messages);
    }

    [Theory]
    [InlineData("\"iyziPaymentId\":123", "\"iyziPaymentId\":\"12x3\"")]
    [InlineData("\"token\":\"00000000-0000-4000-8000-000000000001\"", "\"token\":\"not-a-uuid\"")]
    [InlineData("\"paymentConversationId\":\"dv0123456789abcdef0123456789abcdef\"", "\"paymentConversationId\":\"conversation-456\"")]
    [InlineData("\"iyziEventType\":\"CHECKOUT_FORM_AUTH\"", "\"iyziEventType\":\"BANK_TRANSFER_AUTH\"")]
    public async Task CheckoutFieldsOutsideTheSupportedUnambiguousShapeAreRejected(string expected, string replacement)
    {
        var inbox = new FakeInboxWriter();
        var body = KnownHppBody.Replace(expected, replacement, StringComparison.Ordinal);

        var result = await CreateIngestor(inbox).IngestAsync(Encoding.UTF8.GetBytes(body), KnownHppSignature, default);

        Assert.Equal(PaymentWebhookIngestionOutcome.InvalidPayload, result);
        Assert.Empty(inbox.Messages);
    }

    private static IyzicoPaymentWebhookIngestor CreateIngestor(FakeInboxWriter inbox) => new(
        Options.Create(new IyzicoWebhookOptions { WebhookSecretKey = "testSecret", MerchantId = "3404590" }),
        inbox,
        new FixedClock());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeInboxWriter : IProviderEventInboxWriter
    {
        public List<InboxRecord> Messages { get; } = [];
        public bool Duplicate { get; init; }
        public bool ThrowStorageFailure { get; init; }

        public Task<ProviderEventInboxWriteOutcome> AppendAsync(
            string providerName,
            string providerEventId,
            string minimizedPayload,
            DateTimeOffset receivedAt,
            CancellationToken cancellationToken)
        {
            Messages.Add(new InboxRecord(providerName, providerEventId, minimizedPayload, receivedAt));
            if (ThrowStorageFailure) throw new InvalidOperationException("storage unavailable");
            return Task.FromResult(Duplicate
                ? ProviderEventInboxWriteOutcome.Duplicate
                : ProviderEventInboxWriteOutcome.Accepted);
        }

        public sealed record InboxRecord(string ProviderName, string ProviderEventId, string Payload, DateTimeOffset ReceivedAt);
    }
}
