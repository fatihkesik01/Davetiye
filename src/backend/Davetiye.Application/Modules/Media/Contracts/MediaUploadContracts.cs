using Davetiye.Domain.Modules.Media;

namespace Davetiye.Application.Modules.Media.Contracts;

public sealed record MediaUploadRequest(
    Guid AssetId,
    MediaKind Kind,
    MediaQuotaScope QuotaScope,
    long MaximumBytes,
    DateTimeOffset ExpiresAt,
    long MaximumDurationSeconds = 0);

/// <summary>Short-lived bearer capability for a server-controlled upload ingress; never a delivery URL.</summary>
public sealed record MediaUploadCapability(
    Uri IngressUri,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string>? IngressHeaders = null);

/// <summary>
/// Provider-neutral video upload boundary. For a given <c>AssetId</c>, capability creation must
/// be idempotent: retries return the same single-upload capability (or an equivalent capability
/// with identical asset, byte ceiling and original expiry), and must never extend its lifetime or
/// authorize a second object/key. Image uploads use
/// <see cref="IMediaImageNormalizationPipeline"/> and must not go through a raw direct-to-origin
/// PUT capability.
/// </summary>
public interface IMediaUploadGateway
{
    Task<MediaUploadCapability> CreateVideoCapabilityAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken);

    Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken);

    Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken);
}

/// <summary>
/// Port for a server-side image normalization pipeline. The returned ingress must commit only the
/// normalized bytes to private provider storage; original image bytes must never be persisted.
/// A provider-owned gateway (for example, an edge worker transformation) may implement this port.
/// It has the same per-asset idempotent, single-upload capability requirements as
/// <see cref="IMediaUploadGateway"/>.
/// </summary>
public interface IMediaImageNormalizationPipeline
{
    Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a typed receipt only after server-side inspection confirms the normalized output is
    /// the stored object. A browser upload-complete callback is not sufficient evidence.
    /// </summary>
    Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(
        Guid assetId,
        CancellationToken cancellationToken);
}

public sealed record MediaVerificationReservation(
    Guid AssetId,
    Guid InvitationId,
    MediaKind Kind,
    MediaAssetState State,
    string? ProviderObjectReference,
    long DeclaredByteLength,
    long MaximumByteLength,
    long MaximumDurationSeconds,
    DateTimeOffset IntentExpiresAt);

public sealed record MediaProviderVideoInspection(
    string ProviderUid,
    string State,
    bool ReadyToStream,
    long? ByteLength,
    int? DurationSeconds,
    string? ContentType,
    MediaVerificationEvidence? Evidence);

public enum MediaFinalizeResult
{
    Ready,
    Pending,
    Rejected,
    NotFound,
    Forbidden,
    Duplicate
}

public interface IMediaVerificationStore
{
    Task<MediaVerificationReservation?> LoadCreatorAssetAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken);
    Task<MediaVerificationReservation?> LoadAssetAsync(Guid assetId, CancellationToken cancellationToken);
    Task<MediaVerificationReservation?> LoadGuestAssetAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken);
    Task<bool> IsProviderEventProcessedAsync(string eventId, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid assetId, string providerObjectReference, MediaVerificationEvidence? videoEvidence,
        NormalizedImageVerificationEvidence? imageEvidence, string? providerEventId, DateTimeOffset now,
        bool rejected, CancellationToken cancellationToken);
}

public interface IStreamWebhookSignatureVerifier
{
    bool IsEnabled { get; }
    bool IsValid(byte[] rawBody, string signatureHeader, DateTimeOffset now);
}

public interface IMediaVerificationService
{
    Task<MediaFinalizeResult> FinalizeImageAsync(Guid accountId, Guid invitationId, Guid assetId, CancellationToken cancellationToken);
    Task<MediaFinalizeResult> ProcessStreamWebhookAsync(Guid assetId, string providerUid, string state,
        bool readyToStream, CancellationToken cancellationToken);
}

/// <summary>Quota usage is resolved independently for each scope; Creator and Guest usage never merge.</summary>
public sealed record MediaQuotaUsage(MediaQuotaScope Scope, long Images, long Videos, long PendingUploads);

public interface IMediaQuotaReader
{
    Task<MediaQuotaUsage> GetUsageAsync(
        Guid invitationId,
        MediaQuotaScope scope,
        CancellationToken cancellationToken);
}
