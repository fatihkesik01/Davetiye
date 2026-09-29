using Davetiye.Domain.Modules.IntegrationFoundation;

namespace Davetiye.Application.Modules.IntegrationFoundation.Contracts;

/// <summary>
/// Typed outbox persistence contract (docs/PHASE_0_BASELINE.md §2's "Typed inbox/outbox repository"
/// narrow contract). A producing business module (out of this milestone's scope) appends a message
/// as part of its own transaction; this module only guarantees durable, retry-safe delivery of what
/// was appended.
/// </summary>
public interface IOutboxMessageRepository : IMessageClaimStore<OutboxMessage>
{
    Task AppendAsync(OutboxMessage message, CancellationToken cancellationToken);
}
