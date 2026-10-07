namespace Davetiye.Application.Modules.Notifications.Contracts;

/// <summary>Rendered email content. This contract stays inside Infrastructure delivery.</summary>
public sealed record EmailDeliveryMessage(string ToEmail, string Subject, string Html, string Text);

/// <summary>Provider-neutral email transport. Implementations should not log message content.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailDeliveryMessage message, Guid idempotencyId, CancellationToken cancellationToken);
}
