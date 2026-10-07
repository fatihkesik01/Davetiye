using System.Text.Json;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class MediaLifecycleJobs(DavetiyeDbContext db, IOutboxWorkStore outbox,
    IMediaProviderAssetMaintenance provider, IMediaLifecycleMetrics metrics, IClock clock,
    IOptions<MediaLifecycleJobOptions> options, MediaReconciliationCursor reconciliationCursor,
    ILogger<MediaLifecycleJobs> logger, IMemoryUploadExpirySweeper? memoryUploadSweeper = null) : IMediaLifecycleJobs
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MediaLifecycleJobOptions settings = options.Value;

    public async Task<MediaLifecycleRunResult> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.UtcNow.ToUniversalTime();
        // Abandon memories whose guest upload capability expired first (their open assets become Rejected, bytes and quota kept, PD-16).
        var abandonedMemories = memoryUploadSweeper is null ? 0 : await memoryUploadSweeper.SweepAsync(batchSize, cancellationToken);
        var expiredIntentsClosed = await CloseExpiredIntentsAsync(batchSize, now, cancellationToken);
        metrics.RecordExpiredIntents(expiredIntentsClosed);
        await ReconcileTerminalDeletionsAsync(batchSize, cancellationToken);

        var claimed = await outbox.ClaimAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, batchSize,
            TimeSpan.FromSeconds(settings.ClaimLeaseSeconds), now, cancellationToken);
        var succeeded = 0;
        var retriedOrTerminal = 0;
        foreach (var work in claimed)
        {
            var deletionNow = clock.UtcNow.ToUniversalTime();
            var deletion = TryReadDeletionPayload(work.Payload);
            var latestUploadExpiry = deletion?.DeleteNotBefore;
            if (latestUploadExpiry is null && deletion?.Version == 1)
            {
                // Compatibility for v1 work created before purge began snapshotting intent expiry.
                // Once its MediaAsset row is purged there is no safe way to recover the old expiry.
                latestUploadExpiry = await db.PendingUploads.AsNoTracking()
                    .Where(intent => intent.MediaAssetId == GetAssetId(work.Payload))
                    .MaxAsync(intent => (DateTimeOffset?)intent.ExpiresAt, cancellationToken);
                // Older purge versions removed the intent row before saving a v1 message. Their exact
                // expiry is unrecoverable, so conservatively wait the hard maximum capability lifetime
                // from purge creation (which is later than intent creation) before deleting provider bytes.
                latestUploadExpiry ??= work.CreatedAt.AddMinutes(MediaOutboxMessageTypes.LegacyCapabilityMaximumLifetimeMinutes)
                    .AddSeconds(1); // Outbox timestamps are rounded down to PostgreSQL's microsecond precision.
            }
            if (latestUploadExpiry is not null && latestUploadExpiry > deletionNow)
            {
                // The intent row is intentionally gone by now. Its captured expiry remains in the durable
                // deletion payload so a still-valid in-flight upload cannot recreate orphaned provider bytes.
                await outbox.FailAsync(work.Receipt, deletionNow, latestUploadExpiry, cancellationToken);
                retriedOrTerminal++;
                continue;
            }

            if (await DeleteProviderAssetAsync(work, cancellationToken))
            {
                // Provider calls can outlive the claim lease. Acknowledge against the current
                // clock so the outbox owner can reject stale receipts instead of accepting a
                // completion timestamp captured before the external call.
                var completedAt = clock.UtcNow.ToUniversalTime();
                try
                {
                    await using var completion = await db.Database.BeginTransactionAsync(cancellationToken);
                    var assetId = GetAssetId(work.Payload);
                    var asset = assetId == Guid.Empty ? null : await db.MediaAssets
                        .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken);
                    if (asset?.State == MediaAssetState.PendingDeletion) asset.ConfirmProviderDeletion(completedAt);
                    await db.SaveChangesAsync(cancellationToken);
                    await outbox.CompleteAsync(work.Receipt, completedAt, cancellationToken);
                    await completion.CommitAsync(cancellationToken);
                    succeeded++;
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogInformation(exception, "A media deletion completed after its outbox lease was lost; it will be retried idempotently.");
                }
            }
            else
            {
                var failedAt = clock.UtcNow.ToUniversalTime();
                try
                {
                    await outbox.FailAsync(work.Receipt, failedAt, ComputeNextAttempt(work.AttemptCount, failedAt), cancellationToken);
                    retriedOrTerminal++;
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogInformation(exception, "A failed media deletion lost its outbox lease before retry scheduling.");
                }
            }
        }

        var (reconciled, drift, failures) = await ReconcileReadyAssetsAsync(batchSize, cancellationToken);
        await RefreshOperationalMeasurementsAsync(clock.UtcNow.ToUniversalTime(), cancellationToken);
        return new(expiredIntentsClosed, claimed.Count, succeeded, retriedOrTerminal, reconciled, drift, failures, abandonedMemories);
    }

    private async Task<int> CloseExpiredIntentsAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await (from intent in db.PendingUploads
            join asset in db.MediaAssets on intent.MediaAssetId equals asset.Id
            where intent.ConsumedAt == null && intent.CancelledAt == null && intent.ExpiresAt <= now &&
                  asset.State == MediaAssetState.PendingUpload
            orderby intent.ExpiresAt, intent.Id
            select new { Intent = intent, Asset = asset })
            .Take(batchSize).ToListAsync(cancellationToken);
        foreach (var item in expired)
        {
            item.Intent.CancelExpired(now);
            item.Asset.Reject();
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<bool> DeleteProviderAssetAsync(ClaimedOutboxWork work, CancellationToken cancellationToken)
    {
        try
        {
            var payload = TryReadDeletionPayload(work.Payload);
            if (payload is null || !Guid.TryParseExact(payload.AssetId, "N", out var assetId) || assetId == Guid.Empty ||
                payload.Version == 2 && (!Guid.TryParseExact(payload.ProviderAssetId, "N", out var providerAssetId) || providerAssetId != assetId ||
                                         payload.Kind is null || !Enum.IsDefined(payload.Kind.Value) || payload.DeleteNotBefore is null))
                throw new InvalidDataException("A media deletion outbox payload was invalid.");

            var deletion = await provider.DeleteAsync(assetId, cancellationToken);
            metrics.RecordDeletionResult(deletion == MediaProviderDeletionResult.AlreadyAbsent ? "already_absent" : "deleted");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            metrics.RecordDeletionResult("retryable_failure");
            logger.LogWarning(exception, "A media provider deletion attempt failed.");
            return false;
        }
    }

    private static Guid GetAssetId(string payload)
    {
        var deletion = TryReadDeletionPayload(payload);
        return deletion is { Version: 1 or 2 } && Guid.TryParseExact(deletion.AssetId, "N", out var assetId)
            ? assetId
            : Guid.Empty;
    }

    private static PermanentMediaDeletionPayload? TryReadDeletionPayload(string payload)
    {
        try
        {
            var deletion = JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(payload, JsonOptions);
            return deletion is { Version: 1 or 2 } ? deletion : null;
        }
        catch (JsonException) { return null; }
    }

    private async Task ReconcileTerminalDeletionsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var lastMessageId = reconciliationCursor.ReadTerminal();
        var terminal = await outbox.GetTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
            batchSize, lastMessageId, cancellationToken);
        if (terminal.Count == 0 && lastMessageId is not null)
        {
            reconciliationCursor.ResetTerminal();
            terminal = await outbox.GetTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, batchSize, null, cancellationToken);
        }
        if (terminal.Count > 0) reconciliationCursor.AdvanceTerminal(terminal[^1].MessageId);
        foreach (var work in terminal)
        {
            var payload = TryReadDeletionPayload(work.Payload);
            if (payload is not { Version: 2, Kind: not null } ||
                !Guid.TryParseExact(payload.AssetId, "N", out var assetId) || assetId == Guid.Empty ||
                !Guid.TryParseExact(payload.ProviderAssetId, "N", out var providerAssetId) || providerAssetId != assetId ||
                payload.DeleteNotBefore is null)
            {
                // V1 did not persist kind/provider identity. Keep it terminal and visible for explicit retry,
                // but do not guess which provider inspection endpoint is authoritative.
                metrics.RecordReconciliation("terminal_identity_unavailable");
                continue;
            }

            if (payload.DeleteNotBefore > clock.UtcNow.ToUniversalTime())
            {
                // A terminal failure does not override the capability safety window. Provider absence
                // can change before the bearer upload expires, so keep the message terminal and defer
                // inspection/resolution until the same boundary enforced by normal claims.
                metrics.RecordReconciliation("terminal_capability_active");
                continue;
            }

            try
            {
                var presence = await provider.InspectAsync(assetId, payload.Kind.Value, cancellationToken);
                if (presence == MediaProviderAssetPresence.Absent)
                {
                    if (await outbox.ResolveTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
                            work.MessageId, clock.UtcNow.ToUniversalTime(), cancellationToken))
                        metrics.RecordReconciliation("terminal_absent_resolved");
                }
                else
                {
                    metrics.RecordReconciliation(presence == MediaProviderAssetPresence.Present
                        ? "terminal_present" : "terminal_invalid");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                metrics.RecordReconciliation("terminal_inspection_failed");
                logger.LogWarning(exception, "A terminal media deletion reconciliation inspection failed.");
            }
        }
    }

    private DateTimeOffset? ComputeNextAttempt(int attemptCount, DateTimeOffset now)
    {
        var nextAttemptNumber = attemptCount + 1;
        if (nextAttemptNumber >= settings.MaximumAttempts)
        {
            metrics.RecordDeletionResult("terminal_failure");
            return null;
        }
        var exponent = Math.Min(attemptCount, 30);
        var delay = Math.Min(settings.MaximumRetryDelaySeconds,
            settings.InitialRetryDelaySeconds * Math.Pow(2, exponent));
        return now.AddSeconds(delay);
    }

    private async Task<(int Reconciled, int Drift, int Failures)> ReconcileReadyAssetsAsync(int batchSize,
        CancellationToken cancellationToken)
    {
        var (lastCreatedAt, lastId) = reconciliationCursor.Read();
        var query = db.MediaAssets.AsNoTracking()
            .Where(asset => asset.State == MediaAssetState.Ready || asset.State == MediaAssetState.Rejected ||
                            asset.State == MediaAssetState.PendingDeletion);
        var assets = await ReadReconciliationPageAsync(query, lastCreatedAt, lastId, batchSize, cancellationToken);
        if (assets.Count == 0 && lastCreatedAt is not null)
        {
            reconciliationCursor.Reset();
            assets = await ReadReconciliationPageAsync(query, null, Guid.Empty, batchSize, cancellationToken);
        }
        if (assets.Count > 0) reconciliationCursor.Advance(assets[^1].CreatedAt, assets[^1].AssetId);
        var drift = 0;
        var failures = 0;
        foreach (var asset in assets)
        {
            try
            {
                var presence = await provider.InspectAsync(asset.AssetId, asset.Kind, cancellationToken);
                var outcome = asset.State == MediaAssetState.Ready
                    ? presence switch
                    {
                        MediaProviderAssetPresence.Present => "ready_present",
                        MediaProviderAssetPresence.Absent => "ready_missing",
                        _ => "ready_invalid",
                    }
                    : presence switch
                    {
                        MediaProviderAssetPresence.Present => "retained_present",
                        MediaProviderAssetPresence.Absent => "retained_absent",
                        _ => "retained_invalid",
                    };
                metrics.RecordReconciliation(outcome);
                if (outcome is "ready_missing" or "ready_invalid") drift++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                failures++;
                metrics.RecordReconciliation("inspection_failed");
                logger.LogWarning(exception, "A media provider reconciliation inspection failed.");
            }
        }
        return (assets.Count, drift, failures);
    }

    private static Task<List<MediaReconciliationCandidate>> ReadReconciliationPageAsync(IQueryable<MediaAsset> query,
        DateTimeOffset? lastCreatedAt, Guid lastId, int batchSize, CancellationToken cancellationToken)
    {
        if (lastCreatedAt is not null)
        {
            var cursorDate = lastCreatedAt.Value;
            query = query.Where(asset => asset.CreatedAt > cursorDate || asset.CreatedAt == cursorDate && asset.Id.CompareTo(lastId) > 0);
        }
        return query.OrderBy(asset => asset.CreatedAt).ThenBy(asset => asset.Id).Take(batchSize)
            .Select(asset => new MediaReconciliationCandidate(asset.Id, asset.Kind, asset.State, asset.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task RefreshOperationalMeasurementsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var assetGroups = await (
            from asset in db.MediaAssets.AsNoTracking()
            join upload in db.PendingUploads.AsNoTracking() on asset.Id equals upload.MediaAssetId into uploads
            from upload in uploads.DefaultIfEmpty()
            where asset.State == MediaAssetState.Ready || asset.State == MediaAssetState.Rejected ||
                  asset.State == MediaAssetState.PendingDeletion
            group new { Asset = asset, Upload = upload } by asset.State into stateGroup
            select new
            {
                State = stateGroup.Key,
                Count = stateGroup.LongCount(),
                StorageUpperBoundBytes = stateGroup.Sum(item => item.Asset.ByteLength ??
                    (item.Upload == null ? 0L : item.Upload.MaximumByteLength)),
            })
            .ToListAsync(cancellationToken);
        foreach (var state in new[] { MediaAssetState.Ready, MediaAssetState.Rejected, MediaAssetState.PendingDeletion })
        {
            var aggregate = assetGroups.SingleOrDefault(item => item.State == state);
            metrics.SetAssetSnapshot(ToMetricState(state), aggregate?.Count ?? 0,
                aggregate?.StorageUpperBoundBytes ?? 0);
        }

        var queue = await outbox.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, cancellationToken);
        var oldestAge = queue.OldestPendingCreatedAt is null ? 0 : Math.Max(0, (now - queue.OldestPendingCreatedAt.Value).TotalSeconds);
        metrics.SetDeletionBacklog(queue.PendingCount, oldestAge, queue.TerminalCount);
    }

    private static string ToMetricState(MediaAssetState state) => state switch
    {
        MediaAssetState.Ready => "ready",
        MediaAssetState.Rejected => "rejected",
        MediaAssetState.PendingDeletion => "pending_deletion",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

}
