namespace Davetiye.Application.Modules.Notifications.Contracts;

/// <summary>
/// Provider-neutral transactional email boundary (docs/PRODUCT.md/docs/ARCHITECTURE.md §5:
/// "the application boundary is provider-neutral IEmailSender; changing providers must not change
/// domain behavior"). This milestone (M6a) only needs a minimal, synchronous, direct-send shape: a
/// destination address, a message-kind discriminator (see <see cref="EmailNotificationKinds"/>) and
/// the template data. It is deliberately NOT routed through M5B's inbox/outbox — that is explicit
/// scope for a later milestone that wires real transactional email with a durable-retry provider
/// integration, per docs/ARCHITECTURE.md §5's production transactional-email provider choice.
/// No provider name appears here or in any implementation registered from this milestone.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken);
}
