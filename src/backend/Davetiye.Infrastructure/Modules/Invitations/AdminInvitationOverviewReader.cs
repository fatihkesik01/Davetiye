using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class AdminInvitationOverviewReader(DavetiyeDbContext db, IClock clock) : IAdminInvitationOverviewReader
{
    public async Task<AdminInvitationOverview> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.ToUniversalTime();
        var rows = await (
            from invitation in db.Invitations.AsNoTracking()
            join window in db.PublicationWindows.AsNoTracking().Where(item => item.IsCurrent)
                on invitation.Id equals window.InvitationId into currentWindows
            from window in currentWindows.DefaultIfEmpty()
            select new InvitationLifecycleProjection(
                invitation.State,
                window != null,
                window == null ? null : (DateTimeOffset?)window.StartsAt,
                window == null ? null : (DateTimeOffset?)window.EndsAt))
            .ToListAsync(cancellationToken);

        var counts = new long[Enum.GetValues<InvitationStoredState>().Length];
        foreach (var row in rows)
        {
            var state = InvitationEffectiveStateEvaluator.Evaluate(
                row.StoredState, row.HasCurrentWindow, row.StartsAt, row.EndsAt, now);
            counts[(int)state]++;
        }

        var deleted = await db.Invitations.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(item => item.DeletedAt != null, cancellationToken);

        return new AdminInvitationOverview(
            counts[(int)InvitationStoredState.Draft],
            counts[(int)InvitationStoredState.Scheduled],
            counts[(int)InvitationStoredState.Active],
            counts[(int)InvitationStoredState.Paused],
            counts[(int)InvitationStoredState.Expired],
            deleted);
    }

    private sealed record InvitationLifecycleProjection(
        InvitationStoredState StoredState,
        bool HasCurrentWindow,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt);
}
