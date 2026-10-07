using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class CreatorMediaIntentStore(DavetiyeDbContext dbContext) : ICreatorMediaIntentStore
{
    public async Task<CreatorMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken)
    {
        return await (
            from intent in dbContext.PendingUploads.AsNoTracking()
            join asset in dbContext.MediaAssets.AsNoTracking() on intent.MediaAssetId equals asset.Id
            where intent.IdempotencyKey == key
            select new CreatorMediaReplaySnapshot(
                asset.InvitationId, intent.Id, asset.Id, asset.Kind, asset.State,
                intent.RequestedPresentationRole, intent.DeclaredByteLength, intent.RequestFingerprint,
                intent.ExpiresAt, intent.ConsumedAt, intent.CancelledAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ExpireOpenReservationsAsync(Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await (
            from intent in dbContext.PendingUploads
            join asset in dbContext.MediaAssets on intent.MediaAssetId equals asset.Id
            where asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Creator &&
                  asset.State == MediaAssetState.PendingUpload && intent.ConsumedAt == null &&
                  intent.CancelledAt == null && intent.ExpiresAt <= now
            select new { intent, asset })
            .ToListAsync(cancellationToken);

        foreach (var reservation in expired)
        {
            reservation.intent.CancelExpired(now);
            reservation.asset.Reject();
        }

        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<(long Images, long Videos)> GetCreatorUsageAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        // Provider bytes remain stored until permanent purge confirms deletion. Logical reject or
        // Creator delete therefore cannot release an item slot while those bytes may still exist.
        var counted = new[]
        {
            MediaAssetState.PendingUpload,
            MediaAssetState.Processing,
            MediaAssetState.Ready,
            MediaAssetState.Rejected,
            MediaAssetState.PendingDeletion,
        };
        var counts = await dbContext.MediaAssets.AsNoTracking()
            .Where(asset => asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Creator && counted.Contains(asset.State))
            .GroupBy(asset => asset.Kind)
            .Select(group => new { Kind = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return (counts.Where(item => item.Kind == MediaKind.Image).Sum(item => item.Count),
            counts.Where(item => item.Kind == MediaKind.Video).Sum(item => item.Count));
    }

    public async Task SaveIntentAsync(MediaAsset asset, PendingUpload intent, CancellationToken cancellationToken)
    {
        dbContext.MediaAssets.Add(asset);
        dbContext.PendingUploads.Add(intent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
