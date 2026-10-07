namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Public-code lookup and effective publication gate needed by the RSVP module.</summary>
public interface IRsvpGuestInvitationAccessReader
{
    Task<InvitationRsvpGuestAccess?> ReadAsync(string publicCode, CancellationToken cancellationToken);
    Task<InvitationRsvpGuestAccess?> LockAndReadAsync(string publicCode, CancellationToken cancellationToken);
}

public sealed record InvitationRsvpGuestAccess(
    Guid InvitationId,
    Guid AccountId,
    Guid GrantId,
    DateTimeOffset WindowStartsAt,
    DateTimeOffset WindowEndsAt);
