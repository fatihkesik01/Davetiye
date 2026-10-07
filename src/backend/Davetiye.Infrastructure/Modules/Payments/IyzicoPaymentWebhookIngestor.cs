using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class IyzicoPaymentWebhookIngestor(
    IOptions<IyzicoWebhookOptions> options,
    IProviderEventInboxWriter inbox,
    IClock clock) : IPaymentWebhookIngestor
{
    private const string ProviderName = "iyzico-hpp";

    public async Task<PaymentWebhookIngestionOutcome> IngestAsync(
        ReadOnlyMemory<byte> body,
        string? signature,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) return PaymentWebhookIngestionOutcome.Unavailable;
        if (!IyzicoHppWebhookPayload.TryParse(body, out var payload) || payload is null)
            return PaymentWebhookIngestionOutcome.InvalidPayload;
        if (!IsSignatureValid(payload, signature, settings.WebhookSecretKey!))
            return PaymentWebhookIngestionOutcome.InvalidSignature;

        // MerchantId is not part of iyzico's documented HPP signature input. Check it against the
        // configured account, but never persist or otherwise treat the unsigned body value as proof.
        if (!TryGetMerchantId(body, out var merchantId) ||
            !string.Equals(merchantId, settings.MerchantId, StringComparison.Ordinal))
        {
            return PaymentWebhookIngestionOutcome.InvalidSignature;
        }

        var writeOutcome = await inbox.AppendAsync(
            ProviderName,
            payload.CreateProviderEventId(),
            payload.ToMinimizedJson(),
            clock.UtcNow,
            cancellationToken);
        return writeOutcome == ProviderEventInboxWriteOutcome.Duplicate
            ? PaymentWebhookIngestionOutcome.Duplicate
            : PaymentWebhookIngestionOutcome.Accepted;
    }

    private static bool IsSignatureValid(IyzicoHppWebhookPayload payload, string? signature, string secretKey)
    {
        if (signature is null || signature.Length != 64) return false;
        if (signature.Any(character => !Uri.IsHexDigit(character))) return false;
        var supplied = Convert.FromHexString(signature);
        if (supplied.Length != 32)
            return false;

        // iyzico's official HPP V3 contract prefixes the message with secretKey and also uses that
        // same secret as the HMAC key. The event identity hash intentionally excludes this prefix.
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), payload.GetSignatureMessage(secretKey));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static bool TryGetMerchantId(ReadOnlyMemory<byte> body, out string merchantId)
    {
        merchantId = string.Empty;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;
            var found = false;
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, "merchantId", StringComparison.Ordinal)) continue;
                if (found) return false;
                found = true;
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    merchantId = property.Value.GetString() ?? string.Empty;
                else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number &&
                         property.Value.TryGetInt64(out var value) && value > 0)
                    merchantId = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else
                    return false;
            }

            return found && merchantId.Length is > 0 and <= 32 && merchantId.All(char.IsAsciiDigit);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
