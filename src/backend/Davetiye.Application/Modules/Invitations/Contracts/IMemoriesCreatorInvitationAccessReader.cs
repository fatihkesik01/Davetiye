namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Narrow Memories-module view of invitation ownership and effective lifecycle state (owner-scoped at query level).</summary>
public interface IMemoriesCreatorInvitationAccessReader
{
    Task<InvitationMemoriesCreatorAccess?> GetOwnedEffectiveStateAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<InvitationMemoriesCreatorAccess?> LockOwnedAndGetEffectiveStateAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}

public sealed record InvitationMemoriesCreatorAccess(string EffectiveState);
