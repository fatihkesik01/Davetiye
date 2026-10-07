using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class StreamWebhookSignatureVerifier(IOptions<CloudflareMediaOptions> options)
    : IStreamWebhookSignatureVerifier
{
    private readonly CloudflareMediaOptions settings = options.Value;

    public bool IsEnabled => settings.Enabled && !string.IsNullOrWhiteSpace(settings.StreamWebhookSecret);

    public bool IsValid(byte[] rawBody, string signatureHeader, DateTimeOffset now) =>
        IsEnabled && Verify(rawBody, signatureHeader, settings.StreamWebhookSecret, now,
            TimeSpan.FromSeconds(settings.WebhookMaxAgeSeconds));

    public static bool Verify(byte[] rawBody, string signatureHeader, string secret,
        DateTimeOffset now, TimeSpan maximumAge)
    {
        if (rawBody is null || string.IsNullOrWhiteSpace(secret) || maximumAge <= TimeSpan.Zero || now.Offset != TimeSpan.Zero)
            return false;
        if (!TryParseHeader(signatureHeader, out var timestamp, out var providedSignature)) return false;
        var currentTimestamp = now.ToUnixTimeSeconds();
        if (timestamp > currentTimestamp + 30 || timestamp < currentTimestamp - (long)maximumAge.TotalSeconds) return false;

        var prefix = Encoding.ASCII.GetBytes($"{timestamp}.");
        var message = new byte[prefix.Length + rawBody.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(rawBody, 0, message, prefix.Length, rawBody.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
        try
        {
            var actual = Convert.FromHexString(providedSignature);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseHeader(string value, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        string? timeValue = null;
        string? signatureValue = null;
        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1) return false;
            var key = part[..separator];
            var data = part[(separator + 1)..];
            if (key == "time" && timeValue is null) timeValue = data;
            else if (key == "sig1" && signatureValue is null) signatureValue = data;
            else return false;
        }

        if (timeValue is null || signatureValue is null || signatureValue.Length != 64 ||
            signatureValue.Any(character => !Uri.IsHexDigit(character)) ||
            !long.TryParse(timeValue, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out timestamp) || timestamp <= 0)
            return false;
        signature = signatureValue;
        return true;
    }
}
