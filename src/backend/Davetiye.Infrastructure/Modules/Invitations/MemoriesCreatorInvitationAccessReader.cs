using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Invitation-owned adapter for Memories owner scope and effective lifecycle evaluation.</summary>
public sealed class MemoriesCreatorInvitationAccessReader(DavetiyeDbContext dbContext, IClock clock)
    : IMemoriesCreatorInvitationAccessReader
{
    public async Task<InvitationMemoriesCreatorAccess?> GetOwnedEffectiveStateAsync(Guid accountId, Guid invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Invitations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == invitationId && item.AccountId == accountId && item.DeletedAt == null, cancellationToken);
        if (invitation is null) return null;
        return await EvaluateAsync(invitation, cancellationToken);
    }

    public async Task<InvitationMemoriesCreatorAccess?> LockOwnedAndGetEffectiveStateAsync(Guid accountId, Guid invitationId,
        CancellationToken cancellationToken)
    {
        if (!await InitialPublicationStore.LockOwnedInvitationAndWorkingContentAsync(
                dbContext, accountId, invitationId, cancellationToken))
            return null;
        var invitation = await dbContext.Invitations.SingleAsync(item => item.Id == invitationId && item.AccountId == accountId,
            cancellationToken);
        return await EvaluateAsync(invitation, cancellationToken);
    }

    private async Task<InvitationMemoriesCreatorAccess> EvaluateAsync(Invitation invitation,
        CancellationToken cancellationToken)
    {
        var window = await dbContext.PublicationWindows.AsNoTracking().SingleOrDefaultAsync(item =>
            item.InvitationId == invitation.Id && item.IsCurrent, cancellationToken);
        var state = InvitationEffectiveStateEvaluator.Evaluate(invitation.State, window, clock.UtcNow.ToUniversalTime());
        return new(state.ToString());
    }
}
