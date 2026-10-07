using Davetiye.Domain.Modules.Media;

namespace Davetiye.Application.Modules.Media.Contracts;

public sealed record CreateCreatorMediaIntentCommand(
    Guid AccountId,
    Guid InvitationId,
    Guid IdempotencyKey,
    MediaKind Kind,
    MediaPresentationRole PresentationRole,
    long DeclaredByteLength);

public sealed record CreatorMediaIntentResult(
    Guid IntentId,
    Guid AssetId,
    DateTimeOffset ExpiresAt,
    bool Replayed);

public enum CreatorMediaIntentFailure
{
    NotFound,
    AccountInactive,
    IneligibleInvitation,
    UnsupportedModule,
    InvalidGrant,
    EntitlementLimit,
    QuotaExceeded,
    IdempotencyConflict,
    RateLimited,
    UploadUnavailable,
    InvalidRequest
}

public sealed record CreatorMediaIntentOutcome(
    CreatorMediaIntentResult? Result,
    CreatorMediaIntentFailure? Failure,
    MediaUploadCapability? Capability = null,
    long MaximumDurationSeconds = 0)
{
    public static CreatorMediaIntentOutcome Created(CreatorMediaIntentResult result, MediaUploadCapability capability) => new(result, null, capability);
    public static CreatorMediaIntentOutcome Reserved(CreatorMediaIntentResult result, long maximumDurationSeconds = 0) => new(result, null, MaximumDurationSeconds: maximumDurationSeconds);
    public static CreatorMediaIntentOutcome Rejected(CreatorMediaIntentFailure failure) => new(null, failure);
}

public sealed record CreatorMediaReplaySnapshot(
    Guid InvitationId,
    Guid IntentId,
    Guid AssetId,
    MediaKind Kind,
    MediaAssetState AssetState,
    MediaPresentationRole PresentationRole,
    long DeclaredByteLength,
    string RequestFingerprint,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset? CancelledAt);

public interface ICreatorMediaIntentRateLimiter
{
    bool TryAcquire(Guid accountId, string clientIpAddress);
}

public interface ICreatorMediaIntentStore
{
    Task<CreatorMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken);
    Task ExpireOpenReservationsAsync(Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<(long Images, long Videos)> GetCreatorUsageAsync(Guid invitationId, CancellationToken cancellationToken);
    Task SaveIntentAsync(Davetiye.Domain.Modules.Media.MediaAsset asset, Davetiye.Domain.Modules.Media.PendingUpload intent, CancellationToken cancellationToken);
}

public interface ICreatorMediaIntentService
{
    Task<CreatorMediaIntentOutcome> CreateAsync(CreateCreatorMediaIntentCommand command, string clientIpAddress, CancellationToken cancellationToken);
}
