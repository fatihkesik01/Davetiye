using System.Text.Json;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>
/// Media-owned persistence for Guest-scoped assets (P6-M3). Every query is filtered to <see cref="MediaQuotaScope.Guest"/>
/// so Creator assets can never be read, counted, or discarded through the Guest boundary.
/// </summary>
public sealed class GuestMediaStore(DavetiyeDbContext dbContext, IOutboxWorkStore outbox) : IGuestMediaStore, IGuestMediaAssetStatusReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Provider bytes stay stored until permanent purge confirms deletion, so every state except Deleted keeps consuming
    // Guest quota (PD-16, same rule as Creator).
    private static readonly MediaAssetState[] QuotaCounted =
    [
        MediaAssetState.PendingUpload,
        MediaAssetState.Processing,
        MediaAssetState.Ready,
        MediaAssetState.Rejected,
        MediaAssetState.PendingDeletion
    ];

    public async Task<GuestMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken)
    {
        var row = await (
            from intent in dbContext.PendingUploads.AsNoTracking()
            join asset in dbContext.MediaAssets.AsNoTracking() on intent.MediaAssetId equals asset.Id
            where intent.IdempotencyKey == key && asset.QuotaScope == MediaQuotaScope.Guest
            select new
            {
                asset.InvitationId, asset.Id, asset.Kind, asset.State, intent.RequestFingerprint,
                intent.MaximumByteLength, intent.MaximumDurationSeconds, intent.ExpiresAt, intent.ConsumedAt, intent.CancelledAt
            }).SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new GuestMediaReplaySnapshot(row.InvitationId, row.Id, ToGuestKind(row.Kind),
                row.State == MediaAssetState.PendingUpload, row.RequestFingerprint, row.MaximumByteLength,
                row.MaximumDurationSeconds, row.ExpiresAt, row.ConsumedAt is null && row.CancelledAt is null);
    }

    public async Task<(long Images, long Videos)> GetGuestUsageAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var counts = await dbContext.MediaAssets.AsNoTracking()
            .Where(asset => asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Guest &&
                            QuotaCounted.Contains(asset.State))
            .GroupBy(asset => asset.Kind)
            .Select(group => new { Kind = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return (counts.Where(item => item.Kind == MediaKind.Image).Sum(item => item.Count),
            counts.Where(item => item.Kind == MediaKind.Video).Sum(item => item.Count));
    }

    public async Task<bool> SaveReservationAsync(GuestMediaReservationCommand command, Guid assetId, long maximumBytes,
        long maximumDurationSeconds, string requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var kind = command.Kind == GuestMediaKind.Image ? MediaKind.Image : MediaKind.Video;
        var asset = MediaAsset.CreateGuestAsset(assetId, command.InvitationId, kind, now);
        // The presentation role is a Creator concept; Guest intents store the neutral Gallery value and never get a placement.
        var intent = PendingUpload.Create(Guid.NewGuid(), assetId, command.IdempotencyKey, MediaPresentationRole.Gallery,
            command.DeclaredByteLength, requestFingerprint, now, command.ExpiresAt, maximumBytes, maximumDurationSeconds);
        dbContext.MediaAssets.Add(asset);
        dbContext.PendingUploads.Add(intent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyCollision(exception))
        {
            // PendingUpload keys are globally unique across both Guest and Creator scopes. Another
            // request may have passed its replay lookup before either transaction inserted its key.
            // Translate only this deliberate collision into a safe conflict; never read or return the
            // other scope's asset, and leave the caller's transaction to roll back normally.
            dbContext.Entry(intent).State = EntityState.Detached;
            dbContext.Entry(asset).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsIdempotencyKeyCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_pending_uploads_idempotency_key"
        };

    public async Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assets = await dbContext.MediaAssets
            .Where(asset => asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Guest &&
                            assetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count == 0) return;
        var ids = assets.Select(asset => asset.Id).ToArray();
        var intents = await dbContext.PendingUploads.Where(intent => ids.Contains(intent.MediaAssetId))
            .ToListAsync(cancellationToken);
        foreach (var asset in assets)
        {
            switch (asset.State)
            {
                case MediaAssetState.PendingUpload or MediaAssetState.Processing:
                    asset.Reject();
                    foreach (var intent in intents.Where(item => item.MediaAssetId == asset.Id)) intent.Cancel(now);
                    break;
                case MediaAssetState.Ready:
                    asset.RequestDeletion(now);
                    break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Guest media deletion must run inside the owner deletion transaction.");
        var assets = await dbContext.MediaAssets
            .Where(asset => asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Guest &&
                            assetIds.Contains(asset.Id))
            .OrderBy(asset => asset.Id).ToListAsync(cancellationToken);
        if (assets.Count == 0) return;

        var ids = assets.Select(asset => asset.Id).ToArray();
        var intents = await dbContext.PendingUploads.Where(intent => ids.Contains(intent.MediaAssetId))
            .ToListAsync(cancellationToken);
        foreach (var intent in intents) intent.Cancel(now);

        foreach (var asset in assets.Where(asset => asset.State != MediaAssetState.Deleted))
        {
            asset.RequestDeletion(now);
            var messageId = MediaPurgeCoordinator.StableMessageId(asset.Id);
            var payload = JsonSerializer.Serialize(new PermanentMediaDeletionPayload(1, asset.Id.ToString("N")), JsonOptions);
            await outbox.AppendAsync(new OutboxWorkAppend(messageId, MediaOutboxMessageTypes.PermanentAssetDeletion,
                payload, now), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GuestMediaAssetStatus>> ListAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0) return [];
        var rows = await dbContext.MediaAssets.AsNoTracking()
            .Where(asset => asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Guest &&
                            assetIds.Contains(asset.Id))
            .Select(asset => new { asset.Id, asset.InvitationId, asset.Kind, asset.State })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new GuestMediaAssetStatus(row.Id, row.InvitationId, ToGuestKind(row.Kind), row.State switch
        {
            MediaAssetState.PendingUpload or MediaAssetState.Processing => GuestMediaAssetReadiness.Pending,
            MediaAssetState.Ready => GuestMediaAssetReadiness.Ready,
            MediaAssetState.Rejected => GuestMediaAssetReadiness.Rejected,
            _ => GuestMediaAssetReadiness.Deleted
        })).ToList();
    }

    private static GuestMediaKind ToGuestKind(MediaKind kind) =>
        kind == MediaKind.Image ? GuestMediaKind.Image : GuestMediaKind.Video;

    private sealed record PermanentMediaDeletionPayload(int Version, string AssetId);
}
