using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>Versioned protection for outbox envelopes; no recipient, link, or token is stored in clear text.</summary>
public sealed class EmailOutboxPayloadProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Davetiye.EmailOutbox.v1");

    public string Protect(string toEmail, string kind, IReadOnlyDictionary<string, string> data, DateTimeOffset? expiresAt)
    {
        var plaintext = JsonSerializer.Serialize(new OutboxEmailSender.ProtectedEmailEnvelope(toEmail, kind, data));
        return expiresAt is { } expiry
            ? "tl1:" + _protector.ToTimeLimitedDataProtector().Protect(plaintext, expiry)
            : "v1:" + _protector.Protect(plaintext);
    }

    public (OutboxEmailSender.ProtectedEmailEnvelope Envelope, bool Expired) Unprotect(string payload, DateTimeOffset now)
    {
        string plaintext;
        if (payload.StartsWith("tl1:", StringComparison.Ordinal))
        {
            try
            {
                plaintext = _protector.ToTimeLimitedDataProtector().Unprotect(payload[4..], out var expiration);
                if (expiration <= now)
                    return (null!, true);
            }
            catch (CryptographicException)
            {
                return (null!, true);
            }
        }
        else if (payload.StartsWith("v1:", StringComparison.Ordinal))
        {
            plaintext = _protector.Unprotect(payload[3..]);
        }
        else
        {
            throw new InvalidOperationException("Unknown protected email envelope version.");
        }

        var envelope = JsonSerializer.Deserialize<OutboxEmailSender.ProtectedEmailEnvelope>(plaintext)
            ?? throw new JsonException("Email envelope is empty.");
        if (string.IsNullOrWhiteSpace(envelope.ToEmail) || string.IsNullOrWhiteSpace(envelope.Kind) || envelope.Data is null)
            throw new JsonException("Email envelope is incomplete.");
        return (envelope, false);
    }
}
