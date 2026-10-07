using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Davetiye.Infrastructure.Modules.Payments;

internal sealed record IyzicoHppWebhookPayload(
    string EventType,
    string PaymentId,
    string Token,
    string PaymentConversationId,
    string Status,
    string UntrustedProviderReferenceCode,
    long UntrustedProviderEventTimeUnixMs)
{
    private static readonly HashSet<string> SupportedStatuses = new(StringComparer.Ordinal)
    {
        "FAILURE",
        "SUCCESS",
        "INIT_THREEDS",
        "CALLBACK_THREEDS",
        "BKM_POS_SELECTED",
        "INIT_APM",
        "INIT_BANK_TRANSFER",
        "INIT_CREDIT",
        "PENDING_CREDIT",
        "INIT_CONTACTLESS"
    };

    public static bool TryParse(ReadOnlyMemory<byte> body, out IyzicoHppWebhookPayload? payload)
    {
        payload = null;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetUniqueString(root, "iyziEventType", 64, out var eventType) ||
                !TryGetUniqueScalar(root, "iyziPaymentId", 64, out var paymentId) ||
                !TryGetUniqueString(root, "token", 256, out var token) ||
                !TryGetUniqueString(root, "paymentConversationId", 128, out var conversationId) ||
                !TryGetUniqueString(root, "status", 32, out var status) ||
                !TryGetUniqueString(root, "iyziReferenceCode", 128, out var providerReferenceCode) ||
                !TryGetUniquePositiveInt64(root, "iyziEventTime", out var providerEventTime) ||
                !string.Equals(eventType, "CHECKOUT_FORM_AUTH", StringComparison.Ordinal) ||
                !IsPositiveAsciiInteger(paymentId) ||
                !Guid.TryParseExact(token, "D", out _) ||
                !IsPaymentAttemptReference(conversationId) ||
                !SupportedStatuses.Contains(status))
            {
                return false;
            }

            payload = new IyzicoHppWebhookPayload(eventType, paymentId, token, conversationId, status,
                providerReferenceCode, providerEventTime);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public byte[] GetEventIdentityMessage() => Encoding.UTF8.GetBytes(
        string.Concat(EventType, PaymentId, Token, PaymentConversationId, Status));

    public byte[] GetSignatureMessage(string secretKey) => Encoding.UTF8.GetBytes(
        string.Concat(secretKey, EventType, PaymentId, Token, PaymentConversationId, Status));

    public string CreateProviderEventId() => ComputeProviderEventId(
        EventType, PaymentId, Token, PaymentConversationId, Status);

    public static string ComputeProviderEventId(
        string eventType,
        string paymentId,
        string token,
        string paymentConversationId,
        string status)
    {
        // Keep secret material out of the idempotency key; it hashes only the signed logical tuple.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(eventType, paymentId, token, paymentConversationId, status)));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public string ToMinimizedJson() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        provider = "iyzico",
        format = "hpp",
        eventType = EventType,
        paymentId = PaymentId,
        paymentConversationId = PaymentConversationId,
        status = Status,
        // These fields are outside the official HPP V3 signature input. They are retained only for
        // diagnostics/order investigation and must never drive payment state or event ordering.
        untrustedProviderReferenceCode = UntrustedProviderReferenceCode,
        untrustedProviderEventTimeUnixMs = UntrustedProviderEventTimeUnixMs
    });

    private static bool TryGetUniqueString(JsonElement root, string propertyName, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryGetUniqueProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return IsValidValue(value, maxLength);
    }

    private static bool TryGetUniqueScalar(JsonElement root, string propertyName, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryGetUniqueProperty(root, propertyName, out var element)) return false;

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.Number &&
                 element.TryGetInt64(out var number) && number > 0)
        {
            value = number.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        return IsValidValue(value, maxLength);
    }

    private static bool TryGetUniquePositiveInt64(JsonElement root, string propertyName, out long value)
    {
        value = 0;
        if (!TryGetUniqueProperty(root, propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out value) || value <= 0)
        {
            return false;
        }

        return value <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
    }

    private static bool TryGetUniqueProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal)) continue;
            if (found) return false;
            value = property.Value;
            found = true;
        }

        return found;
    }

    private static bool IsValidValue(string value, int maxLength) =>
        value.Length is > 0 && value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsPositiveAsciiInteger(string value) =>
        value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit) &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 &&
        parsed.ToString(CultureInfo.InvariantCulture) == value;

    private static bool IsPaymentAttemptReference(string value) =>
        value.Length == 34 && value.StartsWith("dv", StringComparison.Ordinal) &&
        value.AsSpan(2).ToString().All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}
