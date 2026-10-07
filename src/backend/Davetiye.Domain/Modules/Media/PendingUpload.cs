namespace Davetiye.Domain.Modules.Media;

/// <summary>A quota-counted, short-lived upload reservation for one MediaAsset.</summary>
public sealed class PendingUpload
{
    private PendingUpload()
    {
    }

    public Guid Id { get; private set; }
    public Guid MediaAssetId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public MediaPresentationRole RequestedPresentationRole { get; private set; }
    public long DeclaredByteLength { get; private set; }
    public long MaximumByteLength { get; private set; }
    public long MaximumDurationSeconds { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public long Revision { get; private set; }

    public static PendingUpload Create(
        Guid id, Guid mediaAssetId, Guid idempotencyKey, MediaPresentationRole requestedPresentationRole,
        long declaredByteLength, string requestFingerprint, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        long maximumByteLength = 0, long maximumDurationSeconds = 0)
    {
        if (id == Guid.Empty || mediaAssetId == Guid.Empty || idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Upload intent and asset identifiers must not be empty.");
        }

        if (maximumByteLength == 0) maximumByteLength = declaredByteLength;
        if (!Enum.IsDefined(requestedPresentationRole) || declaredByteLength <= 0 || maximumByteLength < declaredByteLength ||
            maximumDurationSeconds < 0 ||
            string.IsNullOrWhiteSpace(requestFingerprint) || requestFingerprint.Length != 64 ||
            requestFingerprint.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Upload intent request metadata is invalid.");
        }

        if (createdAt.Offset != TimeSpan.Zero || expiresAt.Offset != TimeSpan.Zero || expiresAt <= createdAt)
        {
            throw new ArgumentException("Upload intent timestamps must be UTC and expiry must follow creation.");
        }

        return new PendingUpload
        {
            Id = id,
            MediaAssetId = mediaAssetId,
            IdempotencyKey = idempotencyKey,
            RequestedPresentationRole = requestedPresentationRole,
            DeclaredByteLength = declaredByteLength,
            MaximumByteLength = maximumByteLength,
            MaximumDurationSeconds = maximumDurationSeconds,
            RequestFingerprint = requestFingerprint.ToLowerInvariant(),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public void Consume(DateTimeOffset consumedAt)
    {
        EnsureOpen(consumedAt);
        ConsumedAt = consumedAt;
        Revision++;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (cancelledAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Cancellation time must be UTC.", nameof(cancelledAt));
        }

        if (ConsumedAt is not null || CancelledAt is not null)
        {
            return;
        }

        CancelledAt = cancelledAt;
        Revision++;
    }

    /// <summary>
    /// Explicitly closes an expired reservation before a retry can reserve the same asset. The
    /// filtered unique index prevents issuing another capability while an expired row is unclosed.
    /// </summary>
    public void CancelExpired(DateTimeOffset cancelledAt)
    {
        if (cancelledAt.Offset != TimeSpan.Zero || cancelledAt < ExpiresAt)
        {
            throw new ArgumentException("An upload can be expired only at or after its UTC expiry time.", nameof(cancelledAt));
        }

        if (ConsumedAt is not null || CancelledAt is not null)
        {
            return;
        }

        CancelledAt = cancelledAt;
        Revision++;
    }

    private void EnsureOpen(DateTimeOffset at)
    {
        // Expiry bounds the upload capability. Provider-confirmed processing may finish later;
        // callers must supply trusted server evidence before consuming the reservation.
        if (at.Offset != TimeSpan.Zero || at < CreatedAt || ConsumedAt is not null || CancelledAt is not null)
        {
            throw new InvalidOperationException("Only an unexpired, open upload intent can be consumed.");
        }
    }
}
