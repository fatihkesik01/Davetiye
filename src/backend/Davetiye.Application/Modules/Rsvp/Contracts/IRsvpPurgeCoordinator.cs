namespace Davetiye.Application.Modules.Rsvp.Contracts;

/// <summary>
/// Deletes RSVP-owned rows for an invitation as part of the invitation's permanent-purge
/// transaction. Implementations must not independently commit that transaction.
/// </summary>
public interface IRsvpPurgeCoordinator
{
    Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken);
}
