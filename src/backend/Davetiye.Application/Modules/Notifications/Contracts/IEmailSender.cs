namespace Davetiye.Application.Modules.Notifications.Contracts;

/// <summary>
/// Provider-neutral durable notification enqueue boundary. Implementations persist protected
/// notification content to the transactional outbox; they do not send synchronously.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken);

    /// <summary>Enqueues once under a caller-owned stable message id, typically in its DB transaction.</summary>
    Task SendOnceAsync(
        Guid messageId,
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken) =>
        SendAsync(toEmail, kind, data, cancellationToken);

    Task SendOnceForAccountAsync(
        Guid ownerAccountId,
        Guid messageId,
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken) =>
        SendOnceAsync(messageId, toEmail, kind, data, cancellationToken);

    /// <summary>
    /// Enqueues a message containing a short-lived authentication link. The protected queue
    /// envelope must expire no later than <paramref name="protectUntilUtc"/>. The default keeps
    /// existing test adapters source-compatible; production infrastructure enforces expiry.
    /// </summary>
    Task SendShortLivedAsync(
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        DateTimeOffset protectUntilUtc,
        CancellationToken cancellationToken) =>
        SendAsync(toEmail, kind, data, cancellationToken);

    Task SendShortLivedForAccountAsync(
        Guid ownerAccountId,
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        DateTimeOffset protectUntilUtc,
        CancellationToken cancellationToken) =>
        SendShortLivedAsync(toEmail, kind, data, protectUntilUtc, cancellationToken);
}
