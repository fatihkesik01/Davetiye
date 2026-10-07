namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Narrow RSVP-module view of invitation ownership and effective lifecycle state.</summary>
public interface IRsvpCreatorInvitationAccessReader
{
    Task<InvitationRsvpEditAccess?> GetOwnedEffectiveStateAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<InvitationRsvpEditAccess?> LockOwnedAndGetEffectiveStateAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}

public sealed record InvitationRsvpEditAccess(string EffectiveState);
