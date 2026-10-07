namespace Davetiye.Domain.Modules.Media;

/// <summary>
/// Metadata and lifecycle for one provider-stored media object. The entity never stores a
/// permanent delivery URL. InvitationId is the sole Creator ownership root; account ownership is
/// derived by joining the Invitation, avoiding duplicate owner attribution that could disagree.
/// Invitation lifecycle remains owned by the Invitations module.
/// </summary>
public sealed class MediaAsset
{
    private MediaAsset()
    {
    }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public MediaQuotaScope QuotaScope { get; private set; }
    public MediaKind Kind { get; private set; }
    public MediaAssetState State { get; private set; }
    public string? ProviderObjectReference { get; private set; }
    public string? DetectedContentType { get; private set; }
    public long? ByteLength { get; private set; }
    public int? DurationSeconds { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? DeletionRequestedAt { get; private set; }
    public DateTimeOffset? ProviderDeletedAt { get; private set; }
    public long Revision { get; private set; }

    public static MediaAsset CreateCreatorAsset(
        Guid id,
        Guid invitationId,
        MediaKind kind,
        DateTimeOffset createdAt) =>
        Create(id, invitationId, MediaQuotaScope.Creator, kind, createdAt);

    /// <summary>A Guest-scoped asset for a memory upload. Guest assets consume only the Guest quota and are never placed by a Creator.</summary>
    public static MediaAsset CreateGuestAsset(
        Guid id,
        Guid invitationId,
        MediaKind kind,
        DateTimeOffset createdAt) =>
        Create(id, invitationId, MediaQuotaScope.Guest, kind, createdAt);

    private static MediaAsset Create(
        Guid id,
        Guid invitationId,
        MediaQuotaScope scope,
        MediaKind kind,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
        {
            throw new ArgumentException("Media and invitation identifiers must not be empty.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Media kind must be supported.");
        }

        EnsureUtc(createdAt, nameof(createdAt));

        return new MediaAsset
        {
            Id = id,
            InvitationId = invitationId,
            QuotaScope = scope,
            Kind = kind,
            State = MediaAssetState.PendingUpload,
            CreatedAt = createdAt
        };
    }

    public void BeginProcessing(string providerObjectReference)
    {
        EnsureState(MediaAssetState.PendingUpload);
        ProviderObjectReference = ValidateProviderReference(providerObjectReference);
        State = MediaAssetState.Processing;
        Revision++;
    }

    /// <summary>
    /// A client completion callback cannot transition an asset to Ready. This method requires a
    /// server-side inspection receipt produced after checking stored bytes. Images use the
    /// separate normalized-image evidence overload, which only the trusted ingress adapter issues.
    /// </summary>
    public void MarkReady(MediaVerificationEvidence evidence, DateTimeOffset readyAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (Kind == MediaKind.Image)
        {
            throw new InvalidOperationException("Image assets require normalized-image verification evidence.");
        }

        MarkReadyCore(
            evidence.ProviderObjectReference,
            evidence.DetectedContentType,
            evidence.ByteLength,
            evidence.DurationSeconds,
            readyAt);
    }

    public void MarkReady(NormalizedImageVerificationEvidence evidence, DateTimeOffset readyAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (Kind != MediaKind.Image)
        {
            throw new InvalidOperationException("Normalized-image evidence cannot verify a video asset.");
        }

        MarkReadyCore(
            evidence.ProviderObjectReference,
            evidence.DetectedContentType,
            evidence.ByteLength,
            durationSeconds: null,
            readyAt);
    }

    public MediaPlacement Place(
        Guid placementId,
        MediaPresentationRole role,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        EnsureState(MediaAssetState.Ready);
        return MediaPlacement.Create(placementId, Id, role, sortOrder, createdAt);
    }

    private void MarkReadyCore(
        string providerObjectReference,
        string detectedContentType,
        long byteLength,
        int? durationSeconds,
        DateTimeOffset readyAt)
    {
        EnsureState(MediaAssetState.Processing);
        EnsureUtc(readyAt, nameof(readyAt));

        var inspectedReference = ValidateProviderReference(providerObjectReference);
        if (ProviderObjectReference is not null &&
            !string.Equals(ProviderObjectReference, inspectedReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider inspection does not match this asset's server-issued object reference.");
        }

        var normalizedContentType = ValidateContentType(detectedContentType);
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Verified media must have positive byte length.");
        }

        if (Kind == MediaKind.Image && !string.Equals(normalizedContentType, "image/webp", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Images must be normalized to the server-approved WebP format.");
        }

        if (Kind == MediaKind.Video &&
            (!IsSafeVideoContentType(normalizedContentType) || durationSeconds is null or <= 0))
        {
            throw new InvalidOperationException("Verified video requires an approved video type and positive duration.");
        }

        ProviderObjectReference = inspectedReference;
        DetectedContentType = normalizedContentType;
        ByteLength = byteLength;
        DurationSeconds = durationSeconds;
        ReadyAt = readyAt;
        State = MediaAssetState.Ready;
        Revision++;
    }

    public void Reject()
    {
        if (State is not (MediaAssetState.PendingUpload or MediaAssetState.Processing))
        {
            throw new InvalidOperationException($"A {State} asset cannot be rejected.");
        }

        State = MediaAssetState.Rejected;
        Revision++;
    }

    public void RequestDeletion(DateTimeOffset requestedAt)
    {
        EnsureUtc(requestedAt, nameof(requestedAt));
        if (State is MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
        {
            return;
        }

        State = MediaAssetState.PendingDeletion;
        DeletionRequestedAt = requestedAt;
        Revision++;
    }

    /// <summary>Called only after a provider delete has been confirmed or reconciled as absent.</summary>
    public void ConfirmProviderDeletion(DateTimeOffset deletedAt)
    {
        EnsureState(MediaAssetState.PendingDeletion);
        EnsureUtc(deletedAt, nameof(deletedAt));
        ProviderDeletedAt = deletedAt;
        State = MediaAssetState.Deleted;
        Revision++;
    }

    private void EnsureState(MediaAssetState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"Expected media state {expected}; actual state is {State}.");
        }
    }

    private static string ValidateProviderReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Provider object reference must be a non-URL opaque identifier.", nameof(value));
        }

        return value.Trim();
    }

    private static string ValidateContentType(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains(';'))
        {
            throw new ArgumentException("A normalized detected media content type is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool IsSafeVideoContentType(string value) => value is
        "video/mp4" or "video/webm" or "video/quicktime" or "video/x-m4v";

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Media timestamps must be UTC.", parameterName);
        }
    }
}
