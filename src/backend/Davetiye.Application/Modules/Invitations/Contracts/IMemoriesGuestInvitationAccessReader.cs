namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>
/// Public-code lookup and effective publication gate needed by the Memories module. Port only in
/// P6-M1; implemented by the Invitations module (P6-M2).
/// Implementations must return null unless the invitation is effective-Active (never Scheduled).
/// </summary>
public interface IMemoriesGuestInvitationAccessReader
{
    Task<InvitationMemoriesGuestAccess?> ReadAsync(string publicCode, CancellationToken cancellationToken);
    Task<InvitationMemoriesGuestAccess?> LockAndReadAsync(string publicCode, CancellationToken cancellationToken);
}

public sealed record InvitationMemoriesGuestAccess(
    Guid InvitationId,
    Guid AccountId,
    Guid GrantId,
    DateTimeOffset WindowStartsAt,
    DateTimeOffset WindowEndsAt);
