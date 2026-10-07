using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Media;

/// <summary>
/// Guest-scoped media reservations for anonymous memory uploads (P6-M3). Limits come only from the effective plan
/// entitlement; the Guest quota is counted separately from the Creator quota (PD-06). The caller owns authorization,
/// the invitation lock and the transaction; this service never calls the provider inside that transaction.
/// </summary>
public sealed class GuestMediaUploadService(
    IGuestMediaStore store,
    IEffectiveEntitlementResolver entitlementResolver,
    IMediaUploadGateway videoUploads,
    IMediaImageNormalizationPipeline imageUploads,
    IClock clock) : IGuestMediaUploadService
{
    public async Task<GuestMediaReservationOutcome> ReserveAsync(
        GuestMediaReservationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow.ToUniversalTime();
        if (command.AccountId == Guid.Empty || command.GrantId == Guid.Empty || command.InvitationId == Guid.Empty ||
            command.OwnerReferenceId == Guid.Empty || command.IdempotencyKey == Guid.Empty ||
            !Enum.IsDefined(command.Kind) || command.DeclaredByteLength <= 0 || command.DeclaredDurationSeconds < 0 ||
            command.ExpiresAt.Offset != TimeSpan.Zero || command.ExpiresAt <= now ||
            command.OwnerItemCount < 0 || command.OwnerItemLimit <= 0 ||
            (command.Kind == GuestMediaKind.Video && command.DeclaredDurationSeconds <= 0))
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.InvalidRequest);
        }

        var result = await entitlementResolver.ResolveAsync(new EntitlementResolutionContext(
            command.AccountId, command.GrantId, command.InvitationId, PublicationEntitlementAction.MemorySubmission),
            cancellationToken);
        if (!result.IsGranted || result.Entitlements is not { MemoriesEnabled: true } grant)
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.InvalidGrant);
        }

        var isVideo = command.Kind == GuestMediaKind.Video;
        var megabytes = isVideo ? grant.MaxGuestVideoSizeMb : grant.MaxGuestImageSizeMb;
        var maximumDuration = isVideo ? grant.MaxGuestVideoDurationSeconds : 0;
        if (megabytes <= 0 || megabytes > long.MaxValue / (1024 * 1024) || (isVideo && maximumDuration <= 0))
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.InvalidGrant);
        }

        var maximumBytes = megabytes * 1024 * 1024;
        // Entitlement ceilings are enforced before any provider call and before anything is persisted.
        if (command.DeclaredByteLength > maximumBytes || (isVideo && command.DeclaredDurationSeconds > maximumDuration))
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.EntitlementLimit);
        }

        var fingerprint = Fingerprint(command);
        var replay = await store.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (replay.InvitationId != command.InvitationId || replay.Kind != command.Kind ||
                !replay.AssetIsOpen || !replay.IntentIsOpen || replay.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(replay.RequestFingerprint), Encoding.ASCII.GetBytes(fingerprint)))
            {
                return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.IdempotencyConflict);
            }

            // Same asset/key; the provider port re-mints an equivalent single-use capability. No new quota is consumed.
            return GuestMediaReservationOutcome.Reserved(new GuestMediaReservation(replay.AssetId, replay.Kind,
                replay.MaximumByteLength, replay.MaximumDurationSeconds, replay.ExpiresAt, Replayed: true));
        }

        if (command.OwnerItemCount >= command.OwnerItemLimit)
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.OwnerLimit);
        }

        var usage = await store.GetGuestUsageAsync(command.InvitationId, cancellationToken);
        var used = isVideo ? usage.Videos : usage.Images;
        var limit = isVideo ? grant.MaxGuestVideos : grant.MaxGuestImages;
        if (used >= limit)
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.QuotaExceeded);
        }

        var assetId = Guid.NewGuid();
        if (!await store.SaveReservationAsync(command, assetId, maximumBytes, maximumDuration, fingerprint, now, cancellationToken))
        {
            return GuestMediaReservationOutcome.Rejected(GuestMediaReservationFailure.IdempotencyConflict);
        }

        return GuestMediaReservationOutcome.Reserved(new GuestMediaReservation(assetId, command.Kind, maximumBytes,
            maximumDuration, command.ExpiresAt, Replayed: false));
    }

    public async Task<MediaUploadCapability?> IssueCapabilityAsync(
        GuestMediaReservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var kind = reservation.Kind == GuestMediaKind.Image ? MediaKind.Image : MediaKind.Video;
        var request = new MediaUploadRequest(reservation.AssetId, kind, MediaQuotaScope.Guest,
            reservation.MaximumBytes, reservation.ExpiresAt, reservation.MaximumDurationSeconds);
        try
        {
            var capability = kind == MediaKind.Image
                ? await imageUploads.CreateNormalizedImageIngressAsync(request, cancellationToken)
                : await videoUploads.CreateVideoCapabilityAsync(request, cancellationToken);
            if (capability is null || !capability.IngressUri.IsAbsoluteUri ||
                capability.IngressUri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(capability.IngressUri.UserInfo) || !string.IsNullOrEmpty(capability.IngressUri.Fragment) ||
                !ValidIngressHeaders(capability.IngressHeaders, kind) ||
                capability.ExpiresAt.Offset != TimeSpan.Zero || capability.ExpiresAt <= clock.UtcNow ||
                capability.ExpiresAt > reservation.ExpiresAt)
            {
                return null;
            }

            return capability;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        return assetIds.Count == 0
            ? Task.CompletedTask
            : store.DiscardAsync(invitationId, assetIds, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }

    public Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        return assetIds.Count == 0
            ? Task.CompletedTask
            : store.DeleteOwnerAssetsAsync(invitationId, assetIds, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }

    private static bool ValidIngressHeaders(IReadOnlyDictionary<string, string>? headers, MediaKind kind)
    {
        if (headers is not null && headers.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Any(char.IsControl) || pair.Value.Length > 4096))
        {
            return false;
        }

        return kind != MediaKind.Image ||
               headers?.Any(pair => string.Equals(pair.Key, "X-Media-Capability", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string Fingerprint(GuestMediaReservationCommand command)
    {
        var canonical = string.Join('|', "guest", command.InvitationId.ToString("N"), command.OwnerReferenceId.ToString("N"),
            command.Kind.ToString(), command.DeclaredByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            command.DeclaredDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
