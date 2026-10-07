using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Payments.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>
/// Authenticated iyzico Checkout Form Retrieve client. It accepts no browser-return data and returns
/// payment fields only after validating the documented response HMAC over payment status, payment
/// identity, amounts, and the stored checkout token.
/// </summary>
public sealed class IyzicoPaymentResultVerifier(
    HttpClient httpClient,
    IOptions<IyzicoPaymentApiOptions> options) : IPaymentResultVerifier
{
    private const string EndpointPath = "/payment/iyzipos/checkoutform/auth/ecom/detail";
    private const int MaximumResponseBytes = 64 * 1024;

    public async Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(
        string paymentId,
        string checkoutToken,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured || !IsPaymentId(paymentId) || !Guid.TryParseExact(checkoutToken, "D", out _) ||
            !IsPaymentReference(conversationId))
            return new(ProviderPaymentVerificationOutcome.Unavailable);

        var body = JsonSerializer.SerializeToUtf8Bytes(new { token = checkoutToken, conversationId, locale = "en" });
        var randomKey = CreateRandomKey();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.ApiBaseUrl), EndpointPath));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation("x-iyzi-rnd", randomKey);
        request.Headers.TryAddWithoutValidation("Authorization", CreateAuthorization(
            settings.ApiKey!, settings.SecretKey!, randomKey, EndpointPath, body));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(ProviderPaymentVerificationOutcome.Unavailable);

            var responseBody = await ReadBoundedAsync(response.Content, MaximumResponseBytes, cancellationToken);
            if (responseBody is null || !TryParseVerifiedResponse(responseBody, settings.SecretKey!, checkoutToken, out var result))
                return new(ProviderPaymentVerificationOutcome.InvalidResponse);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout is a provider outage, not host shutdown. Keep the inbox retryable.
            return new(ProviderPaymentVerificationOutcome.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(ProviderPaymentVerificationOutcome.Unavailable);
        }
        catch (IOException)
        {
            return new(ProviderPaymentVerificationOutcome.Unavailable);
        }
    }

    internal static string CreateAuthorization(
        string apiKey,
        string secretKey,
        string randomKey,
        string path,
        ReadOnlySpan<byte> body)
    {
        var message = new byte[Encoding.UTF8.GetByteCount(randomKey + path) + body.Length];
        var prefix = Encoding.UTF8.GetBytes(randomKey + path);
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), message))
            .ToLowerInvariant();
        var authorizationText = $"apiKey:{apiKey}&randomKey:{randomKey}&signature:{signature}";
        return "IYZWSv2 " + Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationText));
    }

    internal static SocketsHttpHandler CreatePrimaryHandler() => new() { AllowAutoRedirect = false };

    private static bool TryParseVerifiedResponse(
        ReadOnlyMemory<byte> body,
        string secretKey,
        string expectedCheckoutToken,
        out ProviderPaymentVerificationResult result)
    {
        result = new(ProviderPaymentVerificationOutcome.InvalidResponse);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryUniqueString(root, "status", 32, out var requestStatus) ||
                !TryUniqueString(root, "paymentStatus", 32, out var paymentStatus) ||
                !TryUniqueScalar(root, "paymentId", 32, out var paymentId) ||
                !TryUniqueString(root, "token", 64, out var checkoutToken) ||
                !TryUniqueString(root, "currency", 3, out var currency) ||
                !TryUniqueString(root, "basketId", 64, out var basketId) ||
                !TryUniqueString(root, "conversationId", 64, out var conversationId) ||
                !TryUniqueDecimal(root, "paidPrice", out var paidPrice) ||
                !TryUniqueDecimal(root, "price", out var price) ||
                !TryUniqueInt32(root, "fraudStatus", out var fraudStatus) ||
                !TryUniqueString(root, "signature", 64, out var signature) ||
                !IsPaymentId(paymentId) ||
                !string.Equals(checkoutToken, expectedCheckoutToken, StringComparison.Ordinal) ||
                !Guid.TryParseExact(checkoutToken, "D", out _) ||
                !IsIdentifier(currency, 3) ||
                !IsIdentifier(basketId, 64) ||
                !IsIdentifier(conversationId, 64) ||
                requestStatus is not "success" and not "failure" ||
                paymentStatus is not "SUCCESS" and not "FAILURE" and not "INIT_THREEDS" and not "CALLBACK_THREEDS" ||
                fraudStatus is not -1 and not 0 and not 1 ||
                price <= 0m || paidPrice <= 0m ||
                signature.Length != 64 || signature.Any(character => !Uri.IsHexDigit(character)) ||
                !VerifyResponseSignature(signature, secretKey, paymentStatus, paymentId, currency, basketId,
                    conversationId, paidPrice, price, checkoutToken))
            {
                return false;
            }

            // The documented Checkout Form Retrieve response HMAC includes paymentStatus, identity,
            // money fields, and token. Top-level `status` and `fraudStatus` are not in this
            // preimage; they are read only from the credential-authenticated HTTPS response and
            // remain a Phase 11 sandbox/account acceptance gate.
            result = new(
                ProviderPaymentVerificationOutcome.Verified,
                paymentId,
                currency,
                basketId,
                conversationId,
                price,
                paidPrice,
                requestStatus,
                paymentStatus,
                fraudStatus,
                checkoutToken);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyResponseSignature(
        string signature,
        string secretKey,
        string paymentStatus,
        string paymentId,
        string currency,
        string basketId,
        string conversationId,
        decimal paidPrice,
        decimal price,
        string checkoutToken)
    {
        var preimage = string.Join(':', paymentStatus, paymentId, currency, basketId, conversationId,
            FormatProviderMoney(paidPrice), FormatProviderMoney(price), checkoutToken);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(preimage));
        var supplied = Convert.FromHexString(signature);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    internal static string FormatProviderMoney(decimal amount) =>
        amount.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool TryUniqueString(JsonElement root, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryUniqueProperty(root, name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return IsIdentifier(value, maxLength);
    }

    private static bool TryUniqueScalar(JsonElement root, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryUniqueProperty(root, name, out var element)) return false;
        if (element.ValueKind == JsonValueKind.String)
            value = element.GetString() ?? string.Empty;
        else if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number) && number > 0)
            value = number.ToString(CultureInfo.InvariantCulture);
        else
            return false;
        return IsIdentifier(value, maxLength);
    }

    private static bool TryUniqueDecimal(JsonElement root, string name, out decimal value)
    {
        value = 0m;
        if (!TryUniqueProperty(root, name, out var element)) return false;
        var text = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryUniqueInt32(JsonElement root, string name, out int value)
    {
        value = 0;
        return TryUniqueProperty(root, name, out var element) &&
            element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryUniqueProperty(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
            if (found) return false;
            value = property.Value;
            found = true;
        }
        return found;
    }

    private static bool IsIdentifier(string value, int maxLength) =>
        value.Length is > 0 && value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsPaymentId(string value) =>
        value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit) &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 &&
        parsed.ToString(CultureInfo.InvariantCulture) == value;

    private static bool IsPaymentReference(string value) => value.Length == 34 &&
        value.StartsWith("dv", StringComparison.Ordinal) &&
        value.AsSpan(2).ToString().All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static string CreateRandomKey() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
        Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
