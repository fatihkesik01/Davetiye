using Davetiye.Application.Modules.Analytics.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Analytics;

public sealed class InvitationViewCounter(DavetiyeDbContext dbContext) : IInvitationViewCounter
{
    public async Task RecordAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        // One atomic UPSERT counts concurrent successful renders without lost increments. The
        // invitation FK cascades on purge; no request identifiers or visitor data are persisted.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO invitation_view_totals (invitation_id, total)
            SELECT id, 1 FROM invitations WHERE id = {invitationId} AND deleted_at IS NULL
            ON CONFLICT (invitation_id) DO UPDATE SET total = invitation_view_totals.total + 1
            """, cancellationToken);
    }

    public async Task<long> ReadAsync(Guid invitationId, CancellationToken cancellationToken) =>
        await dbContext.InvitationViewTotals.AsNoTracking().Where(total => total.InvitationId == invitationId)
            .Select(total => (long?)total.Total).SingleOrDefaultAsync(cancellationToken) ?? 0;
}
