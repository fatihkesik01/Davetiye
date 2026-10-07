using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class MediaPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Integration_foundation_outbox_append_is_replay_safe_and_claim_completion_is_type_scoped()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var store = new OutboxWorkStore(context);
        var now = DateTimeOffset.UtcNow;
        var persistedNow = now.AddTicks(-(now.Ticks % 10));
        const string mediaType = "media.permanent-asset-deletion.v1";
        const string staleType = "media.stale-claim-test.v1";
        var work = new OutboxWorkAppend(Guid.NewGuid(), mediaType, "{\"assetId\":\"safe-id\"}", now);
        var staleWork = new OutboxWorkAppend(Guid.NewGuid(), staleType, "{}", now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(work, default));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AppendAsync(work, default);
            await store.AppendAsync(work, default); // Same deterministic work ID is an idempotent replay.
            await store.AppendAsync(new OutboxWorkAppend(Guid.NewGuid(), "email.rsvp-confirmation", "{}", now), default);
            await store.AppendAsync(staleWork, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(work with { Payload = "{}" }, default));
            await transaction.CommitAsync();
        }

        var stats = await store.GetStatisticsAsync(mediaType, default);
        Assert.Equal(1, stats.PendingCount);
        Assert.Equal(0, stats.TerminalCount);
        Assert.Equal(persistedNow, stats.OldestPendingCreatedAt);

        var claimed = await store.ClaimAsync(mediaType, 10, TimeSpan.FromMinutes(2), now, default);
        var receipt = Assert.Single(claimed).Receipt;
        Assert.Equal(work.Payload, claimed[0].Payload);
        await store.FailAsync(receipt, now, now.AddSeconds(1), default);
        var retry = await store.ClaimAsync(mediaType, 10, TimeSpan.FromMinutes(2), now.AddSeconds(2), default);
        await store.CompleteAsync(Assert.Single(retry).Receipt, now.AddSeconds(2), default);

        stats = await store.GetStatisticsAsync(mediaType, default);
        Assert.Equal(0, stats.PendingCount);
        Assert.Equal(0, stats.TerminalCount);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(message => message.MessageType == "email.rsvp-confirmation"));
        Assert.Empty(await store.ClaimAsync(mediaType, 10, TimeSpan.FromMinutes(2), now.AddSeconds(3), default));

        var originalClaim = Assert.Single(await store.ClaimAsync(staleType, 10, TimeSpan.FromMinutes(2), now, default));
        var reclaimedAt = now.AddMinutes(3);
        var replacementClaim = Assert.Single(await store.ClaimAsync(staleType, 10, TimeSpan.FromMinutes(2), reclaimedAt, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(originalClaim.Receipt, reclaimedAt, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FailAsync(originalClaim.Receipt, reclaimedAt, reclaimedAt.AddSeconds(5), default));
        await store.CompleteAsync(replacementClaim.Receipt, reclaimedAt.AddSeconds(1), default);
    }

    [Fact]
    public async Task Lifecycle_closes_only_expired_open_intents_and_reports_aggregate_reconciliation_drift_and_failures()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('d', Davetiye.Domain.Modules.Invitations.PublicInvitationCode.EncodedLength), now);
        var ready = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Image, now.AddMinutes(-3));
        ready.BeginProcessing("ready-image");
        ready.MarkReady(new NormalizedImageVerificationEvidence("ready-image", "image/webp", 2048), now.AddMinutes(-2));
        var expired = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now.AddMinutes(-2));
        var expiredIntent = PendingUpload.Create(Guid.NewGuid(), expired.Id, Guid.NewGuid(), MediaPresentationRole.Gallery,
            4096, new string('e', 64), now.AddMinutes(-2), now.AddMinutes(-1));
        context.Invitations.Add(invitation);
        context.MediaAssets.AddRange(ready, expired);
        context.PendingUploads.Add(expiredIntent);
        await context.SaveChangesAsync();

        var metrics = new LifecycleMetricsSpy();
        var provider = new ReconciliationProviderSpy(expired.Id);
        var jobs = new MediaLifecycleJobs(context, new OutboxWorkStore(context), provider, metrics,
            new FixedClock(now), Options.Create(new MediaLifecycleJobOptions()), new MediaReconciliationCursor(),
            NullLogger<MediaLifecycleJobs>.Instance);
        var result = await jobs.RunBatchAsync(20, default);

        Assert.Equal(1, result.ExpiredIntentsClosed);
        Assert.Equal(now, (await context.PendingUploads.SingleAsync(item => item.Id == expiredIntent.Id)).CancelledAt);
        Assert.Equal(MediaAssetState.Rejected, (await context.MediaAssets.SingleAsync(item => item.Id == expired.Id)).State);
        Assert.Equal(0, await context.OutboxMessages.CountAsync(message => message.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion));
        Assert.Equal(0, provider.DeleteCalls);
        Assert.Equal(2, result.Reconciled);
        Assert.Equal(1, result.ProviderDrift);
        Assert.Equal(1, result.ReconciliationFailures);
        Assert.Contains("ready_missing", metrics.ReconciliationOutcomes);
        Assert.Contains("inspection_failed", metrics.ReconciliationOutcomes);
        Assert.DoesNotContain(ready.Id.ToString("N"), string.Join(',', metrics.ReconciliationOutcomes));
    }

    [Fact]
    public async Task Invitation_media_purge_keeps_latest_upload_expiry_after_intent_and_asset_rows_are_removed()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(2);
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('a', PublicInvitationCode.EncodedLength), now);
        var asset = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        var intent = CreateIntent(asset.Id, now, expiry);
        context.Invitations.Add(invitation);
        context.MediaAssets.Add(asset);
        context.PendingUploads.Add(intent);
        await context.SaveChangesAsync();

        var coordinator = new MediaPurgeCoordinator(context, new OutboxWorkStore(context), new FixedClock(now));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var result = await coordinator.EnqueueAndRemoveInvitationAssetsAsync(invitation.Id, default);
            Assert.Equal(1, result.Assets);
            await transaction.CommitAsync();
        }

        Assert.False(await context.PendingUploads.AnyAsync(item => item.Id == intent.Id));
        Assert.False(await context.MediaAssets.AnyAsync(item => item.Id == asset.Id));
        var work = await context.OutboxMessages.SingleAsync(item => item.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion);
        var payload = System.Text.Json.JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(work.Payload,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Version);
        Assert.Equal(asset.Id.ToString("N"), payload.AssetId);
        Assert.Equal(asset.Id.ToString("N"), payload.ProviderAssetId);
        Assert.Equal(MediaKind.Video, payload.Kind);
        Assert.Equal(expiry, payload.DeleteNotBefore);

        var clock = new MutableClock(now);
        var provider = new ReconciliationProviderSpy(Guid.Empty);
        var store = new OutboxWorkStore(context);
        var jobs = new MediaLifecycleJobs(context, store, provider, new LifecycleMetricsSpy(), clock,
            Options.Create(new MediaLifecycleJobOptions()), new MediaReconciliationCursor(), NullLogger<MediaLifecycleJobs>.Instance);
        var deferred = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, deferred.DeletionMessagesClaimed);
        Assert.Equal(1, deferred.DeletionsRetried);
        Assert.Equal(0, provider.DeleteCalls);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).PendingCount);

        clock.Current = expiry.AddSeconds(1);
        context.ChangeTracker.Clear();
        var deleted = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, deleted.DeletionMessagesClaimed);
        Assert.Equal(1, deleted.DeletionsSucceeded);
        Assert.Equal(1, provider.DeleteCalls);
        Assert.Equal(0, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).PendingCount);
    }

    [Fact]
    public async Task Invitation_purge_upgrades_existing_guest_v1_deletion_work_without_conflicting_or_losing_expiry()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(2);
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('b', PublicInvitationCode.EncodedLength), now);
        var asset = MediaAsset.CreateGuestAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        var intent = CreateIntent(asset.Id, now, expiry);
        context.Invitations.Add(invitation);
        context.MediaAssets.Add(asset);
        context.PendingUploads.Add(intent);
        await context.SaveChangesAsync();
        var store = new OutboxWorkStore(context);

        await using (var guestDelete = await context.Database.BeginTransactionAsync())
        {
            await new GuestMediaStore(context, store).DeleteOwnerAssetsAsync(invitation.Id, [asset.Id], now, default);
            await guestDelete.CommitAsync();
        }
        var legacyWork = await context.OutboxMessages.SingleAsync(item => item.Id == MediaPurgeCoordinator.StableMessageId(asset.Id));
        using (var legacyPayload = System.Text.Json.JsonDocument.Parse(legacyWork.Payload))
            Assert.Equal(1, legacyPayload.RootElement.GetProperty("version").GetInt32());

        await using (var purge = await context.Database.BeginTransactionAsync())
        {
            await new MediaPurgeCoordinator(context, store, new FixedClock(now))
                .EnqueueAndRemoveInvitationAssetsAsync(invitation.Id, default);
            await purge.CommitAsync();
        }

        Assert.Equal(1, await context.OutboxMessages.CountAsync(item =>
            item.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion && item.Id == MediaPurgeCoordinator.StableMessageId(asset.Id)));
        Assert.False(await context.PendingUploads.AnyAsync(item => item.Id == intent.Id));
        Assert.False(await context.MediaAssets.AnyAsync(item => item.Id == asset.Id));
        var upgraded = await context.OutboxMessages.AsNoTracking()
            .SingleAsync(item => item.Id == MediaPurgeCoordinator.StableMessageId(asset.Id));
        var payload = System.Text.Json.JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(upgraded.Payload,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Version);
        Assert.Equal(asset.Id.ToString("N"), payload.ProviderAssetId);
        Assert.Equal(MediaKind.Video, payload.Kind);
        Assert.Equal(expiry, payload.DeleteNotBefore);

        var clock = new MutableClock(now);
        var provider = new ReconciliationProviderSpy(Guid.Empty);
        var jobs = new MediaLifecycleJobs(context, store, provider, new LifecycleMetricsSpy(), clock,
            Options.Create(new MediaLifecycleJobOptions()), new MediaReconciliationCursor(), NullLogger<MediaLifecycleJobs>.Instance);
        await jobs.RunBatchAsync(10, default);
        Assert.Equal(0, provider.DeleteCalls);
        clock.Current = expiry.AddSeconds(1);
        context.ChangeTracker.Clear();
        var completed = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, completed.DeletionsSucceeded);
        Assert.Equal(1, provider.DeleteCalls);
    }

    [Fact]
    public async Task Legacy_v1_deletion_without_intent_row_waits_the_full_capability_grace()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var createdAt = DateTimeOffset.UtcNow;
        var clock = new MutableClock(createdAt);
        var assetId = Guid.NewGuid();
        var store = new OutboxWorkStore(context);
        var payload = System.Text.Json.JsonSerializer.Serialize(new { Version = 1, AssetId = assetId.ToString("N") });
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AppendAsync(new OutboxWorkAppend(MediaPurgeCoordinator.StableMessageId(assetId),
                MediaOutboxMessageTypes.PermanentAssetDeletion, payload, createdAt), default);
            await transaction.CommitAsync();
        }

        var provider = new ReconciliationProviderSpy(Guid.Empty);
        var jobs = new MediaLifecycleJobs(context, store, provider, new LifecycleMetricsSpy(), clock,
            Options.Create(new MediaLifecycleJobOptions()), new MediaReconciliationCursor(), NullLogger<MediaLifecycleJobs>.Instance);
        var deferred = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, deferred.DeletionsRetried);
        Assert.Equal(0, provider.DeleteCalls);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).PendingCount);

        clock.Current = createdAt.AddMinutes(MediaOutboxMessageTypes.LegacyCapabilityMaximumLifetimeMinutes).AddSeconds(2);
        var completed = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, completed.DeletionsSucceeded);
        Assert.Equal(1, provider.DeleteCalls);
    }

    [Fact]
    public async Task Terminal_provider_deletion_stays_visible_reconciles_and_can_be_explicitly_retried()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableClock(now);
        var store = new OutboxWorkStore(context);
        var assetId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new PermanentMediaDeletionPayload(2,
            assetId.ToString("N"), assetId.ToString("N"), MediaKind.Image, now));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AppendAsync(new OutboxWorkAppend(MediaPurgeCoordinator.StableMessageId(assetId),
                MediaOutboxMessageTypes.PermanentAssetDeletion, payload, now), default);
            await transaction.CommitAsync();
        }

        var provider = new TerminalRetryProvider { DeleteFailuresRemaining = 1, Presence = MediaProviderAssetPresence.Present };
        var metrics = new LifecycleMetricsSpy();
        var jobs = new MediaLifecycleJobs(context, store, provider, metrics, clock,
            Options.Create(new MediaLifecycleJobOptions { MaximumAttempts = 1 }), new MediaReconciliationCursor(),
            NullLogger<MediaLifecycleJobs>.Instance);

        var first = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, first.DeletionsRetried);
        var terminal = await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default);
        Assert.Equal(0, terminal.PendingCount);
        Assert.Equal(1, terminal.TerminalCount);

        await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, provider.InspectCalls);
        Assert.Contains("terminal_present", metrics.ReconciliationOutcomes);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).TerminalCount);

        Assert.False(await store.RetryTerminalAsync("email.unrelated", MediaPurgeCoordinator.StableMessageId(assetId),
            clock.UtcNow, default));
        Assert.True(await store.RetryTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
            MediaPurgeCoordinator.StableMessageId(assetId), clock.UtcNow, default));
        Assert.False(await store.RetryTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
            MediaPurgeCoordinator.StableMessageId(assetId), clock.UtcNow, default));
        provider.DeleteFailuresRemaining = 1;
        var retried = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, retried.DeletionsRetried);
        Assert.Equal(2, provider.DeleteCalls);
        provider.Presence = MediaProviderAssetPresence.Absent;
        clock.Current = clock.Current.AddSeconds(1);
        await jobs.RunBatchAsync(10, default);
        var reconciled = await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default);
        Assert.Equal(0, reconciled.PendingCount);
        Assert.Equal(0, reconciled.TerminalCount);
        Assert.Contains("terminal_absent_resolved", metrics.ReconciliationOutcomes);
        Assert.Equal(2, provider.DeleteCalls); // Confirmed absence resolves without another provider delete.
    }

    [Fact]
    public async Task Terminal_absence_reconciliation_and_retry_respect_capability_expiry()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddSeconds(10);
        var clock = new MutableClock(now);
        var store = new OutboxWorkStore(context);
        var assetId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new PermanentMediaDeletionPayload(2,
            assetId.ToString("N"), assetId.ToString("N"), MediaKind.Image, expiry));
        var messageId = MediaPurgeCoordinator.StableMessageId(assetId);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AppendAsync(new OutboxWorkAppend(messageId, MediaOutboxMessageTypes.PermanentAssetDeletion,
                payload, now), default);
            await transaction.CommitAsync();
        }

        // Seed the state left behind by exhausted retries while the upload capability is still live.
        var initialClaim = Assert.Single(await store.ClaimAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
            1, TimeSpan.FromMinutes(1), now, default));
        await store.FailAsync(initialClaim.Receipt, now, null, default);

        var provider = new TerminalRetryProvider { Presence = MediaProviderAssetPresence.Absent };
        var metrics = new LifecycleMetricsSpy();
        var jobs = new MediaLifecycleJobs(context, store, provider, metrics, clock,
            Options.Create(new MediaLifecycleJobOptions { MaximumAttempts = 1 }), new MediaReconciliationCursor(),
            NullLogger<MediaLifecycleJobs>.Instance);

        await jobs.RunBatchAsync(10, default);
        Assert.Equal(0, provider.InspectCalls);
        Assert.Contains("terminal_capability_active", metrics.ReconciliationOutcomes);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).TerminalCount);

        // Explicit retry reopens the same durable payload, but the regular claim path must still defer it.
        Assert.True(await store.RetryTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, messageId, now, default));
        var deferredRetry = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, deferredRetry.DeletionsRetried);
        Assert.Equal(0, provider.DeleteCalls);
        Assert.Equal(0, provider.InspectCalls);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).PendingCount);

        clock.Current = expiry.AddSeconds(1);
        provider.DeleteFailuresRemaining = 1;
        var failedAfterExpiry = await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, failedAfterExpiry.DeletionsRetried);
        Assert.Equal(1, provider.DeleteCalls);
        Assert.Equal(1, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).TerminalCount);

        await jobs.RunBatchAsync(10, default);
        Assert.Equal(1, provider.InspectCalls);
        Assert.Contains("terminal_absent_resolved", metrics.ReconciliationOutcomes);
        var resolved = await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default);
        Assert.Equal(0, resolved.PendingCount);
        Assert.Equal(0, resolved.TerminalCount);
    }

    [Fact]
    public async Task Lifecycle_storage_upper_bound_uses_reservation_maximum_for_rejected_asset_without_verified_size()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('a', Davetiye.Domain.Modules.Invitations.PublicInvitationCode.EncodedLength), now);
        var rejected = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now.AddMinutes(-2));
        rejected.Reject();
        var reservationMaximum = 96_000_000L;
        var intent = PendingUpload.Create(Guid.NewGuid(), rejected.Id, Guid.NewGuid(), MediaPresentationRole.Gallery,
            12_000_000, new string('f', 64), now.AddMinutes(-2), now.AddMinutes(10), reservationMaximum, 900);
        context.Invitations.Add(invitation);
        context.MediaAssets.Add(rejected);
        context.PendingUploads.Add(intent);
        await context.SaveChangesAsync();

        var metrics = new LifecycleMetricsSpy();
        var jobs = new MediaLifecycleJobs(context, new OutboxWorkStore(context), new ReconciliationProviderSpy(Guid.Empty),
            metrics, new FixedClock(now), Options.Create(new MediaLifecycleJobOptions()), new MediaReconciliationCursor(),
            NullLogger<MediaLifecycleJobs>.Instance);
        await jobs.RunBatchAsync(10, default);

        Assert.Equal((1L, reservationMaximum), metrics.AssetSnapshots["rejected"]);
    }

    [Fact]
    public async Task Creator_usage_keeps_rejected_and_pending_deletion_assets_until_confirmed_provider_deletion()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('b', Davetiye.Domain.Modules.Invitations.PublicInvitationCode.EncodedLength), now);

        var pendingImage = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Image, now);
        var processingVideo = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        processingVideo.BeginProcessing("processing-video");
        var readyImage = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Image, now);
        readyImage.BeginProcessing("ready-image");
        readyImage.MarkReady(new NormalizedImageVerificationEvidence("ready-image", "image/webp", 2048), now);
        var rejectedVideo = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        rejectedVideo.Reject();
        var pendingDeletionImage = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Image, now);
        pendingDeletionImage.RequestDeletion(now);
        var deletedVideo = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        deletedVideo.RequestDeletion(now);
        deletedVideo.ConfirmProviderDeletion(now);

        context.Invitations.Add(invitation);
        context.MediaAssets.AddRange(pendingImage, processingVideo, readyImage, rejectedVideo, pendingDeletionImage, deletedVideo);
        await context.SaveChangesAsync();

        var usage = await new CreatorMediaIntentStore(context).GetCreatorUsageAsync(invitation.Id, default);

        Assert.Equal(3, usage.Images); // PendingUpload, Ready, PendingDeletion.
        Assert.Equal(2, usage.Videos); // Processing, Rejected; confirmed Deleted releases its slot.
    }

    [Fact]
    public async Task Lifecycle_does_not_ack_expired_claims_after_provider_latency_and_continues_the_batch()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableClock(now);
        var store = new OutboxWorkStore(context);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            for (var index = 0; index < 2; index++)
            {
                var id = Guid.NewGuid();
                var payload = System.Text.Json.JsonSerializer.Serialize(new { Version = 1, AssetId = id.ToString("N") });
                await store.AppendAsync(new OutboxWorkAppend(id, MediaOutboxMessageTypes.PermanentAssetDeletion, payload,
                    now.AddMinutes(-MediaOutboxMessageTypes.LegacyCapabilityMaximumLifetimeMinutes - 1)), default);
            }
            await transaction.CommitAsync();
        }

        var provider = new LeaseAdvancingProvider(clock);
        var jobs = new MediaLifecycleJobs(context, store, provider, new LifecycleMetricsSpy(), clock,
            Options.Create(new MediaLifecycleJobOptions { ClaimLeaseSeconds = 1, MaximumAttempts = 3 }),
            new MediaReconciliationCursor(), NullLogger<MediaLifecycleJobs>.Instance);
        var slowBatch = await jobs.RunBatchAsync(10, default);

        Assert.Equal(2, slowBatch.DeletionMessagesClaimed);
        Assert.Equal(0, slowBatch.DeletionsSucceeded);
        Assert.Equal(0, slowBatch.DeletionsRetried);
        Assert.Equal(2, provider.DeleteCalls); // Lost lease for one receipt did not abort the next claim.
        var queued = await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default);
        Assert.Equal(2, queued.PendingCount);
        Assert.Equal(0, await context.OutboxMessages.CountAsync(message => message.ProcessedAt != null));

        provider.AdvanceClockOnFirstCall = false;
        var retryBatch = await jobs.RunBatchAsync(10, default);
        Assert.Equal(2, retryBatch.DeletionMessagesClaimed);
        Assert.Equal(2, retryBatch.DeletionsSucceeded);
        Assert.Equal(4, provider.DeleteCalls);
        Assert.Equal(0, (await store.GetStatisticsAsync(MediaOutboxMessageTypes.PermanentAssetDeletion, default)).PendingCount);

        var failedAssetId = Guid.NewGuid();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { Version = 1, AssetId = failedAssetId.ToString("N") });
            await store.AppendAsync(new OutboxWorkAppend(failedAssetId, MediaOutboxMessageTypes.PermanentAssetDeletion, payload,
                clock.UtcNow.AddMinutes(-MediaOutboxMessageTypes.LegacyCapabilityMaximumLifetimeMinutes - 1)), default);
            await transaction.CommitAsync();
        }
        provider.FailAndAdvanceNextCall = true;
        var delayedFailureJobs = new MediaLifecycleJobs(context, store, provider, new LifecycleMetricsSpy(), clock,
            Options.Create(new MediaLifecycleJobOptions { ClaimLeaseSeconds = 10, InitialRetryDelaySeconds = 5, MaximumAttempts = 3 }),
            new MediaReconciliationCursor(), NullLogger<MediaLifecycleJobs>.Instance);
        await delayedFailureJobs.RunBatchAsync(10, default);
        var failedMessage = await context.OutboxMessages.SingleAsync(message => message.Id == failedAssetId);
        Assert.InRange((failedMessage.NextAttemptAt - clock.UtcNow.AddSeconds(5)).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(9));
    }

    [Fact]
    public async Task Outermost_account_runner_rejects_an_ambient_transaction_before_running_the_operation()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await using var ambient = await context.Database.BeginTransactionAsync();
        var runner = new AccountQuotaTransactionRunner(context);
        var operationCalled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAndCommitAsync(
            Guid.NewGuid(), _ =>
            {
                operationCalled = true;
                return Task.FromResult(true);
            }, CancellationToken.None));

        Assert.False(operationCalled);
    }

    [Fact]
    public async Task Database_allows_only_one_open_upload_intent_per_asset_until_expiry_is_closed()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), new string('b', PublicInvitationCode.EncodedLength), now);
        var asset = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Image, now);
        var originalIntent = CreateIntent(asset.Id, now, now.AddMinutes(1));
        context.Invitations.Add(invitation);
        context.MediaAssets.Add(asset);
        context.PendingUploads.Add(originalIntent);
        await context.SaveChangesAsync();

        await using (var competingRequest = CreateDbContext(connectionString))
        {
            competingRequest.PendingUploads.Add(CreateIntent(asset.Id, now, now.AddMinutes(2)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => competingRequest.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        originalIntent.CancelExpired(now.AddMinutes(1));
        context.PendingUploads.Add(CreateIntent(asset.Id, now.AddMinutes(1), now.AddMinutes(3)));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PendingUploads.CountAsync(item => item.MediaAssetId == asset.Id));
    }

    [Fact]
    public async Task Duplicate_stream_webhook_is_replay_safe_and_does_not_reapply_media_transition()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(),
            new string('c', PublicInvitationCode.EncodedLength), now);
        var asset = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitation.Id, MediaKind.Video, now);
        var intent = CreateIntent(asset.Id, now, now.AddMinutes(15));
        context.Invitations.Add(invitation);
        context.MediaAssets.Add(asset);
        context.PendingUploads.Add(intent);
        await context.SaveChangesAsync();

        const string eventFingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var evidence = new MediaVerificationEvidence("stream-provider-uid", "video/mp4", 2048, 60);
        var store = new MediaVerificationStore(context);
        var first = await store.CompleteAsync(asset.Id, "stream-provider-uid", evidence, null,
            eventFingerprint, now.AddMinutes(1), rejected: false, CancellationToken.None);
        var duplicate = await store.CompleteAsync(asset.Id, "stream-provider-uid", evidence, null,
            eventFingerprint, now.AddMinutes(2), rejected: false, CancellationToken.None);

        Assert.True(first);
        Assert.False(duplicate);
        Assert.Equal(1, await context.MediaProviderEvents.CountAsync(item => item.EventFingerprint == eventFingerprint));
        Assert.Equal(MediaAssetState.Ready, (await context.MediaAssets.AsNoTracking().SingleAsync(item => item.Id == asset.Id)).State);
    }

    [Fact]
    public async Task Trash_retains_media_and_parent_purge_requires_explicit_provider_deletion_first()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var invitation = Invitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), new string('a', PublicInvitationCode.EncodedLength), now);
        var asset = MediaAsset.CreateCreatorAsset(
            Guid.NewGuid(), invitation.Id, MediaKind.Image, now);
        asset.BeginProcessing("opaque-image-object");
        asset.MarkReady(new NormalizedImageVerificationEvidence("opaque-image-object", "image/webp", 4096), now.AddSeconds(1));
        var pending = CreateIntent(asset.Id, now, now.AddMinutes(5));
        var cover = asset.Place(Guid.NewGuid(), MediaPresentationRole.Cover, 0, now);
        var gallery = asset.Place(Guid.NewGuid(), MediaPresentationRole.Gallery, 0, now);

        context.Invitations.Add(invitation);
        context.MediaAssets.Add(asset);
        context.PendingUploads.Add(pending);
        context.MediaPlacements.AddRange(cover, gallery);
        await context.SaveChangesAsync();

        invitation.MoveToTrash(now.AddMinutes(1), now.AddDays(3));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Empty(await context.Invitations.Where(item => item.Id == invitation.Id).ToListAsync());
        Assert.Single(await context.MediaAssets.Where(item => item.InvitationId == invitation.Id).ToListAsync());
        Assert.Single(await context.PendingUploads.Where(item => item.Id == pending.Id).ToListAsync());
        Assert.Equal(2, await context.MediaPlacements.CountAsync(item => item.MediaAssetId == asset.Id));
        Assert.Empty(await context.OutboxMessages.Where(item => item.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion).ToListAsync());

        // Invitation hard purge cannot cascade away the only provider-object reference. The purge
        // coordinator must enqueue/complete provider deletion and remove the media row first.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM invitations WHERE id = $1";
            command.Parameters.AddWithValue(invitation.Id);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("23001", exception.SqlState); // restrict_violation
        }

        var persistedAsset = await context.MediaAssets.SingleAsync(item => item.Id == asset.Id);
        persistedAsset.RequestDeletion(now.AddMinutes(2));
        persistedAsset.ConfirmProviderDeletion(now.AddMinutes(3));
        context.MediaAssets.Remove(persistedAsset);
        await context.SaveChangesAsync(); // confirmed provider deletion allows dropping metadata

        await using var purgeConnection = new NpgsqlConnection(connectionString);
        await purgeConnection.OpenAsync();
        await using var purgeCommand = purgeConnection.CreateCommand();
        purgeCommand.CommandText = "DELETE FROM invitations WHERE id = $1";
        purgeCommand.Parameters.AddWithValue(invitation.Id);
        Assert.Equal(1, await purgeCommand.ExecuteNonQueryAsync());
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options);

    private static PendingUpload CreateIntent(Guid assetId, DateTimeOffset createdAt, DateTimeOffset expiresAt) =>
        PendingUpload.Create(Guid.NewGuid(), assetId, Guid.NewGuid(), MediaPresentationRole.Gallery,
            4096, new string('b', 64), createdAt, expiresAt);

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Current { get; set; } = now;
        public DateTimeOffset UtcNow => Current;
    }
    private sealed class LifecycleMetricsSpy : IMediaLifecycleMetrics
    {
        public List<string> ReconciliationOutcomes { get; } = [];
        public Dictionary<string, (long Count, long StorageUpperBoundBytes)> AssetSnapshots { get; } = new(StringComparer.Ordinal);
        public void RecordExpiredIntents(int count) { }
        public void RecordDeletionResult(string outcome) { }
        public void RecordReconciliation(string outcome) => ReconciliationOutcomes.Add(outcome);
        public void SetAssetSnapshot(string state, long count, long storageUpperBoundBytes) =>
            AssetSnapshots[state] = (count, storageUpperBoundBytes);
        public void SetDeletionBacklog(long pendingCount, double oldestAgeSeconds, long terminalCount) { }
    }
    private sealed class ReconciliationProviderSpy(Guid failingAssetId) : IMediaProviderAssetMaintenance
    {
        public int DeleteCalls { get; private set; }
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.FromResult(MediaProviderDeletionResult.AlreadyAbsent);
        }
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken) =>
            assetId == failingAssetId
                ? Task.FromException<MediaProviderAssetPresence>(new HttpRequestException("Synthetic inspection outage."))
                : Task.FromResult(MediaProviderAssetPresence.Absent);
    }
    private sealed class LeaseAdvancingProvider(MutableClock clock) : IMediaProviderAssetMaintenance
    {
        public int DeleteCalls { get; private set; }
        public bool AdvanceClockOnFirstCall { get; set; } = true;
        public bool FailAndAdvanceNextCall { get; set; }
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            if (AdvanceClockOnFirstCall && DeleteCalls == 1) clock.Current = clock.Current.AddSeconds(2);
            if (FailAndAdvanceNextCall)
            {
                FailAndAdvanceNextCall = false;
                clock.Current = clock.Current.AddSeconds(2);
                return Task.FromException<MediaProviderDeletionResult>(new HttpRequestException("Synthetic provider outage."));
            }
            return Task.FromResult(MediaProviderDeletionResult.Deleted);
        }
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(MediaProviderAssetPresence.Absent);
    }
    private sealed class TerminalRetryProvider : IMediaProviderAssetMaintenance
    {
        public int DeleteFailuresRemaining { get; set; }
        public int DeleteCalls { get; private set; }
        public int InspectCalls { get; private set; }
        public MediaProviderAssetPresence Presence { get; set; }
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            if (DeleteFailuresRemaining-- > 0)
                return Task.FromException<MediaProviderDeletionResult>(new HttpRequestException("Synthetic provider outage."));
            return Task.FromResult(MediaProviderDeletionResult.Deleted);
        }
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken)
        {
            InspectCalls++;
            return Task.FromResult(Presence);
        }
    }
}

