namespace Davetiye.Application.Modules.Memories.Contracts;

/// <summary>
/// Deletes Memories-owned rows for an invitation as part of the invitation's permanent-purge
/// transaction, before Media assets are removed. Implementations must not commit that transaction.
/// </summary>
public interface IMemoriesPurgeCoordinator
{
    Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken);
}
