using Davetiye.Domain.Modules.Media;

namespace Davetiye.Application.Modules.Media.Contracts;

public static class MediaOutboxMessageTypes
{
    public const string PermanentAssetDeletion = "media.permanent-asset-deletion.v1";
    // Creator and Guest upload capabilities currently have a hard maximum lifetime of 15 minutes.
    // V1 purge messages omitted expiry; use this bounded grace from their creation timestamp when
    // the PendingUpload row was already removed by an earlier deployment.
    public const int LegacyCapabilityMaximumLifetimeMinutes = 15;
}

public sealed record MediaPurgeResult(int Assets);
public sealed record MediaLifecycleRunResult(int ExpiredIntentsClosed, int DeletionMessagesClaimed,
    int DeletionsSucceeded, int DeletionsRetried, int Reconciled, int ProviderDrift, int ReconciliationFailures,
    int AbandonedGuestMemories = 0);
public sealed record MediaReconciliationCandidate(Guid AssetId, MediaKind Kind, MediaAssetState State, DateTimeOffset CreatedAt);
/// <summary>
/// Durable provider deletion identity. Version 1 outbox payloads contained only AssetId and remain readable;
/// version 2 snapshots provider identity, media kind, and the latest upload-capability expiry.
/// </summary>
public sealed record PermanentMediaDeletionPayload(int Version, string AssetId,
    string? ProviderAssetId = null, MediaKind? Kind = null, DateTimeOffset? DeleteNotBefore = null);
public enum MediaProviderAssetPresence { Present, Absent, Invalid }
public enum MediaProviderDeletionResult { Deleted, AlreadyAbsent }

/// <summary>Media-owned DB graph maintenance invoked within Invitations' permanent-purge transaction.</summary>
public interface IMediaPurgeCoordinator
{
    Task<MediaPurgeResult> EnqueueAndRemoveInvitationAssetsAsync(Guid invitationId, CancellationToken cancellationToken);
}

public interface IMediaProviderAssetMaintenance
{
    Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken);
    Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken);
}

public interface IMediaLifecycleJobs
{
    Task<MediaLifecycleRunResult> RunBatchAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>Aggregate counters only; implementations must never attach asset/account identifiers as tags.</summary>
public interface IMediaLifecycleMetrics
{
    void RecordExpiredIntents(int count);
    void RecordDeletionResult(string outcome);
    void RecordReconciliation(string outcome);
    void SetAssetSnapshot(string state, long count, long storageUpperBoundBytes);
    void SetDeletionBacklog(long pendingCount, double oldestAgeSeconds, long terminalCount);
}
