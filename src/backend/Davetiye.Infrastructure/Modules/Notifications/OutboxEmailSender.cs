using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>Encrypts notification envelopes before writing them to the shared transactional outbox.</summary>
public sealed class OutboxEmailSender(
    DavetiyeDbContext db,
    IOutboxWorkStore outbox,
    EmailOutboxPayloadProtector payloadProtector,
    IOptions<EmailTokenOptions> emailTokenOptions,
    IClock clock) : IEmailSender
{
    public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (IsAuthenticationMessage(kind))
            throw new InvalidOperationException("Authentication emails must use the short-lived protected enqueue path.");
        return EnqueueAsync(toEmail, kind, data, null, cancellationToken);
    }

    public Task SendOnceAsync(Guid messageId, string toEmail, string kind,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Stable email message id is required.", nameof(messageId));
        if (IsAuthenticationMessage(kind))
            throw new InvalidOperationException("Authentication emails must use the short-lived protected enqueue path.");
        return EnqueueAsync(toEmail, kind, data, null, cancellationToken, messageId);
    }

    public Task SendOnceForAccountAsync(Guid ownerAccountId, Guid messageId, string toEmail, string kind,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        ValidateOwnerAccount(ownerAccountId);
        if (messageId == Guid.Empty)
            throw new ArgumentException("Stable email message id is required.", nameof(messageId));
        if (IsAuthenticationMessage(kind))
            throw new InvalidOperationException("Authentication emails must use the short-lived protected enqueue path.");
        return EnqueueAsync(toEmail, kind, data, null, cancellationToken, messageId, ownerAccountId);
    }

    public Task SendShortLivedAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data,
        DateTimeOffset protectUntilUtc, CancellationToken cancellationToken)
    {
        if (!IsAuthenticationMessage(kind) || protectUntilUtc.Offset != TimeSpan.Zero || protectUntilUtc <= clock.UtcNow)
            throw new ArgumentException("Short-lived protection is only valid for future authentication messages.", nameof(protectUntilUtc));
        var maximum = clock.UtcNow.AddMinutes(emailTokenOptions.Value.TokenLifetimeMinutes);
        if (protectUntilUtc > maximum)
            throw new ArgumentOutOfRangeException(nameof(protectUntilUtc), "Protected authentication content cannot outlive Identity tokens.");
        return EnqueueAsync(toEmail, kind, data, protectUntilUtc, cancellationToken);
    }

    public Task SendShortLivedForAccountAsync(Guid ownerAccountId, string toEmail, string kind,
        IReadOnlyDictionary<string, string> data, DateTimeOffset protectUntilUtc, CancellationToken cancellationToken)
    {
        ValidateOwnerAccount(ownerAccountId);
        if (!IsAuthenticationMessage(kind) || protectUntilUtc.Offset != TimeSpan.Zero || protectUntilUtc <= clock.UtcNow)
            throw new ArgumentException("Short-lived protection is only valid for future authentication messages.", nameof(protectUntilUtc));
        var maximum = clock.UtcNow.AddMinutes(emailTokenOptions.Value.TokenLifetimeMinutes);
        if (protectUntilUtc > maximum)
            throw new ArgumentOutOfRangeException(nameof(protectUntilUtc), "Protected authentication content cannot outlive Identity tokens.");
        return EnqueueAsync(toEmail, kind, data, protectUntilUtc, cancellationToken, ownerAccountId: ownerAccountId);
    }

    private async Task EnqueueAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data,
        DateTimeOffset? protectUntilUtc, CancellationToken cancellationToken, Guid? stableMessageId = null,
        Guid? ownerAccountId = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || toEmail.Length > 320 || string.IsNullOrWhiteSpace(kind) ||
            data is null || data.Count > 32 || data.Any(item => item.Key.Length > 100 || item.Value.Length > 16_384))
            throw new ArgumentException("Email notification data is invalid.");

        var now = clock.UtcNow;
        var encrypted = payloadProtector.Protect(toEmail, kind, data, protectUntilUtc);
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await outbox.AppendAsync(new OutboxWorkAppend(stableMessageId ?? Guid.NewGuid(),
            EmailOutboxWorker.MessageType, encrypted, now, ownerAccountId), cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsAuthenticationMessage(string kind) =>
        kind is EmailNotificationKinds.EmailConfirmation or EmailNotificationKinds.PasswordReset or
            EmailNotificationKinds.AccountDeletionConfirmation;

    private static void ValidateOwnerAccount(Guid ownerAccountId)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("Account-owned email requires an account id.", nameof(ownerAccountId));
    }

    public sealed record ProtectedEmailEnvelope(string ToEmail, string Kind, IReadOnlyDictionary<string, string> Data);
}
