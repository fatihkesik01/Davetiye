using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class MediaPublicationSnapshotReader(DavetiyeDbContext db) : IMediaPublicationSnapshotReader
{
    public async Task<IReadOnlyList<PublicSnapshotMediaPlacement>> ListReadyPlacementsAsync(Guid invitationId,
        CancellationToken cancellationToken) => await (from placement in db.MediaPlacements.AsNoTracking()
        join asset in db.MediaAssets.AsNoTracking() on placement.MediaAssetId equals asset.Id
        where asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Creator && asset.State == MediaAssetState.Ready
        orderby placement.Role, placement.SortOrder
        select new PublicSnapshotMediaPlacement(asset.Id, asset.Kind.ToString(), placement.Role.ToString(), placement.SortOrder))
        .ToListAsync(cancellationToken);
}
