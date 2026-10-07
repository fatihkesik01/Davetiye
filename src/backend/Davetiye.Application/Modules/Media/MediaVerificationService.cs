using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Media;

public sealed class MediaVerificationService(
    IMediaVerificationStore store,
    ICreatorMediaInvitationAccessReader invitationAccessReader,
    IMediaImageNormalizationPipeline imagePipeline,
    IMediaUploadGateway videoGateway,
    IClock clock) : IMediaVerificationService, IGuestMediaVerificationService
{
    public async Task<MediaFinalizeResult> FinalizeImageAsync(
        Guid accountId, Guid invitationId, Guid assetId, CancellationToken cancellationToken)
    {
        var invitation = await invitationAccessReader.LoadAsync(accountId, invitationId, clock.UtcNow, cancellationToken);
        if (invitation is null) return MediaFinalizeResult.NotFound;
        var reservation = await store.LoadCreatorAssetAsync(invitationId, assetId, cancellationToken);
        if (reservation is null) return MediaFinalizeResult.NotFound;
        if (reservation.Kind != MediaKind.Image) return MediaFinalizeResult.Rejected;
        return await VerifyImageAsync(reservation, cancellationToken);
    }

    /// <summary>
    /// Guest assets are verified by pulling provider evidence at finalize (images from the normalizing Worker,
    /// videos from Stream inspection); the caller has already authorized the guest capability for this invitation.
    /// </summary>
    public async Task<MediaFinalizeResult> VerifyAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken)
    {
        var reservation = await store.LoadGuestAssetAsync(invitationId, assetId, cancellationToken);
        if (reservation is null) return MediaFinalizeResult.NotFound;
        return reservation.Kind == MediaKind.Image
            ? await VerifyImageAsync(reservation, cancellationToken)
            : await VerifyVideoByInspectionAsync(reservation, cancellationToken);
    }

    private async Task<MediaFinalizeResult> VerifyImageAsync(
        MediaVerificationReservation reservation, CancellationToken cancellationToken)
    {
        var assetId = reservation.AssetId;
        if (reservation.State == MediaAssetState.Ready) return MediaFinalizeResult.Ready;
        if (reservation.State is not (MediaAssetState.PendingUpload or MediaAssetState.Processing)) return MediaFinalizeResult.Rejected;

        var evidence = await imagePipeline.VerifyStoredImageAsync(assetId, cancellationToken);
        if (evidence is null) return MediaFinalizeResult.Pending;
        if (!SafeOpaqueReference(evidence.ProviderObjectReference) || evidence.ByteLength > reservation.DeclaredByteLength ||
            evidence.ByteLength > reservation.MaximumByteLength || evidence.DetectedContentType != "image/webp")
        {
            await store.CompleteAsync(assetId, evidence.ProviderObjectReference, null, null, null, clock.UtcNow,
                rejected: true, cancellationToken);
            return MediaFinalizeResult.Rejected;
        }

        var completed = await store.CompleteAsync(assetId, evidence.ProviderObjectReference, null, evidence, null,
            clock.UtcNow, rejected: false, cancellationToken);
        return completed ? MediaFinalizeResult.Ready : MediaFinalizeResult.Duplicate;
    }

    public async Task<MediaFinalizeResult> ProcessStreamWebhookAsync(
        Guid assetId, string providerUid, string state, bool readyToStream, CancellationToken cancellationToken)
    {
        if (!SafeOpaqueReference(providerUid)) return MediaFinalizeResult.Rejected;
        var reservation = await store.LoadAssetAsync(assetId, cancellationToken);
        if (reservation is null || reservation.Kind != MediaKind.Video) return MediaFinalizeResult.NotFound;
        if (reservation.State == MediaAssetState.Ready) return MediaFinalizeResult.Ready;
        if (reservation.State is not (MediaAssetState.PendingUpload or MediaAssetState.Processing)) return MediaFinalizeResult.Rejected;

        var inspection = await videoGateway.InspectVideoAsync(assetId, cancellationToken);
        if (inspection is null || !FixedEquals(inspection.ProviderUid, providerUid)) return MediaFinalizeResult.Pending;
        if (!string.Equals(inspection.State, state, StringComparison.Ordinal)) return MediaFinalizeResult.Pending;

        var eventId = EventId(assetId, providerUid, state, readyToStream);
        return await CompleteVideoAsync(reservation, providerUid, state, readyToStream, inspection, eventId, cancellationToken);
    }

    private async Task<MediaFinalizeResult> VerifyVideoByInspectionAsync(
        MediaVerificationReservation reservation, CancellationToken cancellationToken)
    {
        if (reservation.State == MediaAssetState.Ready) return MediaFinalizeResult.Ready;
        if (reservation.State is not (MediaAssetState.PendingUpload or MediaAssetState.Processing)) return MediaFinalizeResult.Rejected;

        var inspection = await videoGateway.InspectVideoAsync(reservation.AssetId, cancellationToken);
        if (inspection is null || !SafeOpaqueReference(inspection.ProviderUid)) return MediaFinalizeResult.Pending;
        return await CompleteVideoAsync(reservation, inspection.ProviderUid, inspection.State, inspection.ReadyToStream,
            inspection, eventId: null, cancellationToken);
    }

    private async Task<MediaFinalizeResult> CompleteVideoAsync(
        MediaVerificationReservation reservation, string providerUid, string state, bool readyToStream,
        MediaProviderVideoInspection inspection, string? eventId, CancellationToken cancellationToken)
    {
        var assetId = reservation.AssetId;
        if (state == "error")
        {
            var rejected = await store.CompleteAsync(assetId, providerUid, null, null, eventId, clock.UtcNow,
                rejected: true, cancellationToken);
            return rejected ? MediaFinalizeResult.Rejected : MediaFinalizeResult.Duplicate;
        }

        if (state != "ready" || !readyToStream || inspection.State != "ready" || !inspection.ReadyToStream ||
            inspection.ByteLength is null or <= 0 || inspection.DurationSeconds is null or <= 0 ||
            inspection.ContentType is null)
        {
            return MediaFinalizeResult.Pending;
        }

        if (inspection.ByteLength > reservation.DeclaredByteLength || inspection.ByteLength > reservation.MaximumByteLength ||
            reservation.MaximumDurationSeconds <= 0 || inspection.DurationSeconds > reservation.MaximumDurationSeconds ||
            !SafeVideoType(inspection.ContentType))
        {
            var rejected = await store.CompleteAsync(assetId, providerUid, null, null, eventId, clock.UtcNow,
                rejected: true, cancellationToken);
            return rejected ? MediaFinalizeResult.Rejected : MediaFinalizeResult.Duplicate;
        }

        if (inspection.Evidence is null || inspection.Evidence.ProviderObjectReference != providerUid ||
            inspection.Evidence.ByteLength != inspection.ByteLength || inspection.Evidence.DurationSeconds != inspection.DurationSeconds)
            return MediaFinalizeResult.Pending;
        var completed = await store.CompleteAsync(assetId, providerUid, inspection.Evidence, null, eventId, clock.UtcNow,
            rejected: false, cancellationToken);
        return completed ? MediaFinalizeResult.Ready : MediaFinalizeResult.Duplicate;
    }

    private static string EventId(Guid assetId, string uid, string state, bool ready) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{assetId:N}|{uid}|{state}|{ready}"))).ToLowerInvariant();

    private static bool SafeOpaqueReference(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
        !Uri.TryCreate(value, UriKind.Absolute, out _) && !value.Any(char.IsControl);

    private static bool SafeVideoType(string value) => value is
        "video/mp4" or "video/webm" or "video/quicktime" or "video/x-m4v";

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
