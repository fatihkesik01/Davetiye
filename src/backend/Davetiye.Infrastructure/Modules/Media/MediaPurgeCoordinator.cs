using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Records provider deletion before Invitation-owned rows are removed, in the caller's transaction.</summary>
public sealed class MediaPurgeCoordinator(DavetiyeDbContext db, IOutboxWorkStore outbox, IClock clock) : IMediaPurgeCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MediaPurgeResult> EnqueueAndRemoveInvitationAssetsAsync(Guid invitationId,
        CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty) throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Media provider deletion must be enqueued inside the permanent-purge transaction.");

        var assets = await db.MediaAssets.Where(item => item.InvitationId == invitationId)
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        if (assets.Count == 0) return new(0);

        var assetIds = assets.Select(item => item.Id).ToArray();
        var placements = await db.MediaPlacements.Where(item => assetIds.Contains(item.MediaAssetId)).ToListAsync(cancellationToken);
        var intents = await db.PendingUploads.Where(item => assetIds.Contains(item.MediaAssetId)).ToListAsync(cancellationToken);
        var latestExpiryByAsset = intents.GroupBy(item => item.MediaAssetId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.ExpiresAt));
        db.MediaPlacements.RemoveRange(placements);
        db.PendingUploads.RemoveRange(intents);

        var now = clock.UtcNow.ToUniversalTime();
        foreach (var asset in assets)
        {
            var messageId = StableMessageId(asset.Id);
            var deleteNotBefore = latestExpiryByAsset.GetValueOrDefault(asset.Id, now);
            await AppendOrUpgradeDeletionAsync(asset, messageId, deleteNotBefore, now, cancellationToken);
        }

        // Save the outbox and media graph changes while the invitation purge transaction is open.
        await db.SaveChangesAsync(cancellationToken);
        db.MediaAssets.RemoveRange(assets);
        await db.SaveChangesAsync(cancellationToken);
        return new(assets.Count);
    }

    public static Guid StableMessageId(Guid assetId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"davetiye/media-delete/v1/{assetId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task AppendOrUpgradeDeletionAsync(Davetiye.Domain.Modules.Media.MediaAsset asset, Guid messageId,
        DateTimeOffset deleteNotBefore, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await outbox.FindAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, messageId,
                cancellationToken);
            if (existing is null)
            {
                var payload = SerializeDeletion(asset, deleteNotBefore);
                try
                {
                    await outbox.AppendAsync(new OutboxWorkAppend(messageId,
                        MediaOutboxMessageTypes.PermanentAssetDeletion, payload, now), cancellationToken);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Another producer may have committed the stable ID between our read and append;
                    // re-read it and merge through the expected-payload update below.
                    continue;
                }
            }

            if (existing.ProcessedAt is not null)
                return; // A previous idempotent deletion already completed at the provider.

            var previous = DeserializeDeletion(existing.Payload);
            if (previous is null || !Guid.TryParseExact(previous.AssetId, "N", out var previousAssetId) ||
                previousAssetId != asset.Id)
                throw new InvalidOperationException("The stable media deletion ID is occupied by an invalid payload.");

            if (previous.Version == 1)
            {
                // Normally the intent rows still exist and supply the exact expiry. If they are gone,
                // conservatively retain the v1 capability grace before upgrading its durable work.
                if (deleteNotBefore <= now)
                    deleteNotBefore = existing.CreatedAt
                        .AddMinutes(MediaOutboxMessageTypes.LegacyCapabilityMaximumLifetimeMinutes).AddSeconds(1);
            }
            else if (previous.Version == 2 &&
                     (!Guid.TryParseExact(previous.ProviderAssetId, "N", out var previousProviderAssetId) ||
                      previousProviderAssetId != asset.Id || previous.Kind != asset.Kind || previous.DeleteNotBefore is null))
            {
                throw new InvalidOperationException("The stable media deletion ID contains conflicting provider identity.");
            }
            else if (previous.Version != 2)
            {
                throw new InvalidOperationException("The stable media deletion ID uses an unsupported payload version.");
            }

            if (previous.Version == 2 && previous.DeleteNotBefore > deleteNotBefore)
                deleteNotBefore = previous.DeleteNotBefore.Value;
            var replacement = SerializeDeletion(asset, deleteNotBefore);
            if (replacement == existing.Payload) return;
            if (await outbox.ReplacePayloadAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, messageId,
                    existing.Payload, replacement, cancellationToken))
                return;
        }

        throw new InvalidOperationException("Media deletion work changed repeatedly while the invitation purge was upgrading it.");
    }

    private static string SerializeDeletion(Davetiye.Domain.Modules.Media.MediaAsset asset, DateTimeOffset deleteNotBefore) =>
        JsonSerializer.Serialize(new PermanentMediaDeletionPayload(2, asset.Id.ToString("N"),
            asset.Id.ToString("N"), asset.Kind, deleteNotBefore), JsonOptions);

    private static PermanentMediaDeletionPayload? DeserializeDeletion(string payload)
    {
        try { return JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(payload, JsonOptions); }
        catch (JsonException) { return null; }
    }

}
