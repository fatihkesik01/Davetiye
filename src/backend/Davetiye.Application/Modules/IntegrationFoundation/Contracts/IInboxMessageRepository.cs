using Davetiye.Domain.Modules.IntegrationFoundation;

namespace Davetiye.Application.Modules.IntegrationFoundation.Contracts;

/// <summary>
/// Typed inbox persistence contract (docs/PHASE_0_PLAN.md §2's "Typed inbox/outbox repository"
/// narrow contract). Appending relies on a real DB unique constraint on
/// (ProviderName, ProviderEventId) to reject a replayed provider event
/// (see <see cref="InboxMessage"/>); this contract does not attempt its own in-memory duplicate
/// check.
/// </summary>
public interface IInboxMessageRepository : IMessageClaimStore<InboxMessage>
{
    Task AppendAsync(InboxMessage message, CancellationToken cancellationToken);
}
