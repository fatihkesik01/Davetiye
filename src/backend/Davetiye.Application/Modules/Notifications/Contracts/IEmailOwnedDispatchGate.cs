using Davetiye.Application.Modules.IntegrationFoundation.Contracts;

namespace Davetiye.Application.Modules.Notifications.Contracts;

public interface IEmailOwnedDispatchGate
{
    Task<OwnedOutboxDispatchOutcome> AuthorizeAsync(
        OutboxClaimReceipt receipt,
        Guid ownerAccountId,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}
