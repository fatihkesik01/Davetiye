using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

public enum PaymentWebhookProcessOutcome
{
    Processed,
    RetryScheduled,
    PermanentlyFailed,
    AlreadyHandled
}

/// <summary>
/// Reconciles normalized HPP events against an authenticated, signature-verified provider lookup.
/// Persistence and all cross-module transaction work stay behind the payment work-store contract.
/// </summary>
public sealed class PaymentWebhookProcessor(
    IPaymentWebhookProcessingStore store,
    IPaymentResultVerifier verifier,
    IClock clock,
    IOptions<PaymentWebhookProcessingOptions> options)
{
    public const string ProviderName = "iyzico-hpp";

    public async Task<PaymentWebhookProcessOutcome> ProcessAsync(
        PaymentWebhookWorkItem work,
        CancellationToken cancellationToken)
    {
        if (!HppInboxEvent.TryParse(work.Payload, out var providerEvent) || providerEvent is null)
            return Map(await FailPermanentlyAsync(work.MessageId, cancellationToken));

        var attempt = await store.FindAttemptAsync(providerEvent.PaymentConversationId, cancellationToken);
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.ProviderCheckoutId) ||
            !MatchesSignedEventKey(work.ProviderEventId, providerEvent, attempt.ProviderCheckoutId))
        {
            return Map(await FailPermanentlyAsync(work.MessageId, cancellationToken));
        }

        if (attempt.Status is "Succeeded" or "Reversed" or "Failed" or "Canceled")
            return Map(await store.MarkHandledAsync(work.MessageId, clock.UtcNow.ToUniversalTime(), cancellationToken));

        var lookup = await verifier.RetrievePaymentAsync(providerEvent.PaymentId, attempt.ProviderCheckoutId,
            attempt.Reference, cancellationToken);
        if (lookup.Outcome == ProviderPaymentVerificationOutcome.Unavailable)
            return Map(await ScheduleRetryAsync(work.MessageId, work.AttemptCount, cancellationToken));
        if (lookup.Outcome != ProviderPaymentVerificationOutcome.Verified || !MatchesAttempt(attempt, providerEvent, lookup))
            return Map(await FailPermanentlyAsync(work.MessageId, cancellationToken));

        var success = lookup.ProviderResponseStatus == "success" &&
                      lookup.PaymentStatus == "SUCCESS" && lookup.FraudStatus == 1;
        var terminalFailure = lookup.ProviderResponseStatus == "success" &&
                              lookup.PaymentStatus == "FAILURE" && lookup.FraudStatus == -1;
        if (!success && !terminalFailure)
            return Map(await ScheduleRetryAsync(work.MessageId, work.AttemptCount, cancellationToken));

        return Map(await store.FinalizeAsync(
            ToFinalizeRequest(work.MessageId, work.ProviderEventId, providerEvent, lookup, clock.UtcNow.ToUniversalTime()),
            cancellationToken));
    }

    private static PaymentWebhookFinalizationRequest ToFinalizeRequest(
        Guid messageId,
        string providerEventId,
        HppInboxEvent providerEvent,
        ProviderPaymentVerificationResult lookup,
        DateTimeOffset processedAtUtc) => new(
        messageId,
        providerEventId,
        providerEvent.EventType,
        providerEvent.PaymentId,
        lookup.PaymentId ?? string.Empty,
        providerEvent.PaymentConversationId,
        providerEvent.Status,
        lookup.Currency ?? string.Empty,
        lookup.BasketId ?? string.Empty,
        lookup.ConversationId ?? string.Empty,
        lookup.Price ?? 0m,
        lookup.PaidPrice ?? 0m,
        lookup.ProviderResponseStatus ?? string.Empty,
        lookup.PaymentStatus ?? string.Empty,
        lookup.FraudStatus ?? int.MinValue,
        processedAtUtc);

    private static bool MatchesAttempt(
        PaymentAttemptVerificationSnapshot attempt,
        HppInboxEvent providerEvent,
        ProviderPaymentVerificationResult result) =>
        result.PaymentId == providerEvent.PaymentId &&
        result.CheckoutToken == attempt.ProviderCheckoutId &&
        result.Currency == attempt.Currency &&
        result.BasketId == attempt.Reference &&
        result.ConversationId == attempt.Reference &&
        result.Price == attempt.Amount &&
        result.PaidPrice == attempt.Amount;

    private static bool MatchesSignedEventKey(
        string providerEventId,
        HppInboxEvent providerEvent,
        string checkoutToken)
    {
        if (!Guid.TryParseExact(checkoutToken, "D", out _) || providerEventId.Length != 64 ||
            providerEventId.Any(character => !Uri.IsHexDigit(character)))
            return false;

        var expected = IyzicoHppWebhookPayload.ComputeProviderEventId(
            providerEvent.EventType, providerEvent.PaymentId, checkoutToken,
            providerEvent.PaymentConversationId, providerEvent.Status);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),
            Convert.FromHexString(providerEventId));
    }

    private async Task<PaymentWebhookStoreOutcome> FailPermanentlyAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await store.RecordFailureAsync(id, clock.UtcNow.ToUniversalTime(), null, cancellationToken);

    private async Task<PaymentWebhookStoreOutcome> ScheduleRetryAsync(
        Guid id,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        DateTimeOffset? nextAt = attemptCount + 1 >= Math.Clamp(settings.MaximumAttempts, 1, 100)
            ? null
            : clock.UtcNow.ToUniversalTime() + RetryDelay(settings, attemptCount);
        return await store.RecordFailureAsync(id, clock.UtcNow.ToUniversalTime(), nextAt, cancellationToken);
    }

    private static TimeSpan RetryDelay(PaymentWebhookProcessingOptions settings, int attemptCount)
    {
        var baseSeconds = Math.Clamp(settings.RetryBaseSeconds, 1, 3600);
        var maxSeconds = Math.Clamp(settings.RetryMaximumSeconds, 1, 86_400);
        return TimeSpan.FromSeconds(Math.Min((double)baseSeconds * Math.Pow(2, Math.Clamp(attemptCount, 0, 16)), maxSeconds));
    }

    private static PaymentWebhookProcessOutcome Map(PaymentWebhookStoreOutcome result) => result switch
    {
        PaymentWebhookStoreOutcome.Processed => PaymentWebhookProcessOutcome.Processed,
        PaymentWebhookStoreOutcome.AlreadyHandled => PaymentWebhookProcessOutcome.AlreadyHandled,
        PaymentWebhookStoreOutcome.RetryScheduled => PaymentWebhookProcessOutcome.RetryScheduled,
        _ => PaymentWebhookProcessOutcome.PermanentlyFailed
    };

    private sealed record HppInboxEvent(
        string EventType,
        string PaymentId,
        string PaymentConversationId,
        string Status)
    {
        private static readonly HashSet<string> SupportedStatuses = new(StringComparer.Ordinal)
        {
            "FAILURE", "SUCCESS", "INIT_THREEDS", "CALLBACK_THREEDS", "BKM_POS_SELECTED",
            "INIT_APM", "INIT_BANK_TRANSFER", "INIT_CREDIT", "PENDING_CREDIT", "INIT_CONTACTLESS"
        };

        public static bool TryParse(string payload, out HppInboxEvent? value)
        {
            value = null;
            try
            {
                using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !TryGetUniqueString(root, "format", out var format) || format != "hpp" ||
                    !TryGetUniqueString(root, "eventType", out var eventType) || eventType != "CHECKOUT_FORM_AUTH" ||
                    !TryGetUniqueString(root, "paymentId", out var paymentId) || !IsPaymentId(paymentId) ||
                    !TryGetUniqueString(root, "paymentConversationId", out var conversationId) ||
                    !IsPaymentReference(conversationId) ||
                    !TryGetUniqueString(root, "status", out var status) || !SupportedStatuses.Contains(status))
                    return false;

                value = new(eventType, paymentId, conversationId, status);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetUniqueString(JsonElement root, string name, out string value)
        {
            value = string.Empty;
            JsonElement foundValue = default;
            var found = false;
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
                if (found || property.Value.ValueKind != JsonValueKind.String) return false;
                found = true;
                foundValue = property.Value;
            }
            if (!found) return false;
            value = foundValue.GetString() ?? string.Empty;
            return value.Length is > 0 and <= 128 && !value.Any(char.IsControl);
        }

        private static bool IsPaymentId(string value) =>
            value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit) &&
            ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
            id.ToString(CultureInfo.InvariantCulture) == value;

        private static bool IsPaymentReference(string value) =>
            value.Length == 34 && value.StartsWith("dv", StringComparison.Ordinal) &&
            value.AsSpan(2).ToString().All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    }
}
