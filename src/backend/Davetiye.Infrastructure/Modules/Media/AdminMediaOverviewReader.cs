using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class AdminMediaOverviewReader(DavetiyeDbContext db) : IAdminMediaOverviewReader
{
    public async Task<AdminMediaOverview> GetAsync(CancellationToken cancellationToken)
    {
        var groups = await db.MediaAssets.AsNoTracking()
            .GroupBy(item => item.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.LongCount(),
                Bytes = group.Sum(item => (long)(item.ByteLength ?? 0))
            })
            .ToListAsync(cancellationToken);
        var counts = groups.ToDictionary(item => item.State, item => item.Count);
        var verifiedBytes = groups
            .Where(item => item.State is MediaAssetState.Ready or MediaAssetState.PendingDeletion)
            .Sum(item => item.Bytes);

        return new AdminMediaOverview(
            groups.Sum(item => item.Count),
            Count(MediaAssetState.Ready),
            Count(MediaAssetState.PendingUpload),
            Count(MediaAssetState.Processing),
            Count(MediaAssetState.PendingDeletion),
            Count(MediaAssetState.Deleted),
            Count(MediaAssetState.Rejected),
            verifiedBytes);

        long Count(MediaAssetState state) => counts.GetValueOrDefault(state);
    }
}
