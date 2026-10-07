using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Invitation-owned, owner-scoped lifecycle context exposed through a narrow application port.</summary>
public sealed class CreatorMediaInvitationAccessReader(DavetiyeDbContext dbContext)
    : ICreatorMediaInvitationAccessReader
{
    public async Task<CreatorMediaInvitationAccessSnapshot?> LoadAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Invitations.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == invitationId && item.AccountId == accountId && item.DeletedAt == null,
                cancellationToken);
        if (invitation is null)
        {
            return null;
        }

        var window = await dbContext.PublicationWindows.AsNoTracking()
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId && item.IsCurrent, cancellationToken);
        var effectiveState = InvitationEffectiveStateEvaluator.Evaluate(invitation.State, window, now);
        return new CreatorMediaInvitationAccessSnapshot(
            (effectiveState is InvitationStoredState.Scheduled or InvitationStoredState.Active) &&
                window?.IsCurrent == true,
            window?.GrantId,
            invitation.TemplateKey);
    }
}
