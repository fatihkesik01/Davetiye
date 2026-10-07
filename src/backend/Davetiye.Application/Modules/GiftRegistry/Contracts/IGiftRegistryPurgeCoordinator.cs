namespace Davetiye.Application.Modules.GiftRegistry.Contracts;

/// <summary>
/// Deletes Gift Registry-owned records for an invitation as part of its permanent-purge
/// transaction. Implementations must not commit that transaction.
/// </summary>
public interface IGiftRegistryPurgeCoordinator
{
    Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken);
}
