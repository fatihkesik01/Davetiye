namespace Davetiye.Application.Modules.Rsvp.Contracts;

/// <summary>Invitation-owned module access is resolved by the Invitations module.</summary>
public interface IRsvpInvitationOwnerReader
{
    Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}
