using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class MediaVerificationStore(DavetiyeDbContext dbContext) : IMediaVerificationStore
{
    public Task<MediaVerificationReservation?> LoadCreatorAssetAsync(
        Guid invitationId, Guid assetId, CancellationToken cancellationToken) =>
        LoadAsync(dbContext.MediaAssets.Where(asset => asset.Id == assetId && asset.InvitationId == invitationId &&
            asset.QuotaScope == MediaQuotaScope.Creator),
            cancellationToken);

    public Task<MediaVerificationReservation?> LoadAssetAsync(Guid assetId, CancellationToken cancellationToken) =>
        LoadAsync(dbContext.MediaAssets.Where(asset => asset.Id == assetId && asset.QuotaScope == MediaQuotaScope.Creator), cancellationToken);

    public Task<MediaVerificationReservation?> LoadGuestAssetAsync(
        Guid invitationId, Guid assetId, CancellationToken cancellationToken) =>
        LoadAsync(dbContext.MediaAssets.Where(asset => asset.Id == assetId && asset.InvitationId == invitationId &&
            asset.QuotaScope == MediaQuotaScope.Guest),
            cancellationToken);

    public Task<bool> IsProviderEventProcessedAsync(string eventId, CancellationToken cancellationToken) =>
        dbContext.MediaProviderEvents.AsNoTracking().AnyAsync(message =>
            message.EventFingerprint == eventId, cancellationToken);

    private async Task<MediaVerificationReservation?> LoadAsync(
        IQueryable<MediaAsset> assets, CancellationToken cancellationToken)
    {
        return await (
            from asset in assets.AsNoTracking()
            join intent in dbContext.PendingUploads.AsNoTracking() on asset.Id equals intent.MediaAssetId
            select new MediaVerificationReservation(asset.Id, asset.InvitationId, asset.Kind, asset.State,
                asset.ProviderObjectReference, intent.DeclaredByteLength, intent.MaximumByteLength,
                intent.MaximumDurationSeconds, intent.ExpiresAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CompleteAsync(
        Guid assetId,
        string providerObjectReference,
        MediaVerificationEvidence? videoEvidence,
        NormalizedImageVerificationEvidence? imageEvidence,
        string? providerEventId,
        DateTimeOffset now,
        bool rejected,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (providerEventId is not null && await dbContext.MediaProviderEvents.AnyAsync(message =>
            message.EventFingerprint == providerEventId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == assetId, cancellationToken);
        var intent = await dbContext.PendingUploads.SingleOrDefaultAsync(candidate => candidate.MediaAssetId == assetId, cancellationToken);
        if (asset is null || intent is null || asset.State is not (MediaAssetState.PendingUpload or MediaAssetState.Processing))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (rejected)
        {
            asset.Reject();
            intent.Cancel(now);
        }
        else
        {
            if (asset.State == MediaAssetState.PendingUpload)
            {
                asset.BeginProcessing(providerObjectReference);
            }

            if (videoEvidence is not null) asset.MarkReady(videoEvidence, now);
            else if (imageEvidence is not null) asset.MarkReady(imageEvidence, now);
            else throw new InvalidOperationException("Verified media evidence is required to mark an asset ready.");

            intent.Consume(now);
            // Guest assets are never placed on an invitation (a Creator-only concept); their visibility is owned by Memories.
            var alreadyPlaced = asset.QuotaScope != MediaQuotaScope.Creator || await dbContext.MediaPlacements.AnyAsync(item =>
                item.MediaAssetId == assetId && item.Role == intent.RequestedPresentationRole, cancellationToken);
            if (!alreadyPlaced)
            {
                dbContext.MediaPlacements.Add(asset.Place(Guid.NewGuid(), intent.RequestedPresentationRole, 0, now));
            }
        }

        if (providerEventId is not null)
        {
            var providerEvent = MediaProviderEvent.Create(Guid.NewGuid(), providerEventId, now);
            providerEvent.MarkProcessed(now);
            dbContext.Set<MediaProviderEvent>().Add(providerEvent);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
