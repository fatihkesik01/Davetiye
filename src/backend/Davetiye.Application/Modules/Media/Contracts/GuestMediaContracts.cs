namespace Davetiye.Application.Modules.Media.Contracts;

/// <summary>
/// Reserves one Guest-scoped asset for one owner (a Memory, opaque to Media). The caller has already authorized the
/// guest and holds the invitation lock inside its own transaction; the reservation participates in that transaction.
/// </summary>
public sealed record GuestMediaReservationCommand(
    Guid AccountId,
    Guid GrantId,
    Guid InvitationId,
    Guid OwnerReferenceId,
    Guid IdempotencyKey,
    GuestMediaKind Kind,
    long DeclaredByteLength,
    long DeclaredDurationSeconds,
    DateTimeOffset ExpiresAt,
    int OwnerItemCount,
    int OwnerItemLimit);

public enum GuestMediaReservationFailure
{
    InvalidRequest,
    InvalidGrant,
    /// <summary>Declared bytes or duration exceed the effective entitlement for this kind.</summary>
    EntitlementLimit,
    /// <summary>The invitation's guest quota for this kind is used up (pending/rejected/pending-deletion count as used).</summary>
    QuotaExceeded,
    /// <summary>The owner already holds its maximum number of items.</summary>
    OwnerLimit,
    IdempotencyConflict
}

public sealed record GuestMediaReservation(
    Guid AssetId,
    GuestMediaKind Kind,
    long MaximumBytes,
    long MaximumDurationSeconds,
    DateTimeOffset ExpiresAt,
    bool Replayed);

public sealed record GuestMediaReservationOutcome(GuestMediaReservation? Reservation, GuestMediaReservationFailure? Failure)
{
    public static GuestMediaReservationOutcome Reserved(GuestMediaReservation reservation) => new(reservation, null);
    public static GuestMediaReservationOutcome Rejected(GuestMediaReservationFailure failure) => new(null, failure);
}

public sealed record GuestMediaReplaySnapshot(
    Guid InvitationId,
    Guid AssetId,
    GuestMediaKind Kind,
    bool AssetIsOpen,
    string RequestFingerprint,
    long MaximumByteLength,
    long MaximumDurationSeconds,
    DateTimeOffset ExpiresAt,
    bool IntentIsOpen);

/// <summary>Media-owned persistence behind <see cref="IGuestMediaUploadService"/>.</summary>
public interface IGuestMediaStore
{
    Task<GuestMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken);
    Task<(long Images, long Videos)> GetGuestUsageAsync(Guid invitationId, CancellationToken cancellationToken);
    /// <summary>Persists the reservation; returns false when the globally unique idempotency key raced with another request.</summary>
    Task<bool> SaveReservationAsync(GuestMediaReservationCommand command, Guid assetId, long maximumBytes, long maximumDurationSeconds,
        string requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken);
    Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now, CancellationToken cancellationToken);
    Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Guest upload boundary used by Memories. Provider capabilities are minted through the Phase 4 gateway ports with
/// <c>MediaQuotaScope.Guest</c> and are bound to exactly one asset/key.
/// </summary>
public interface IGuestMediaUploadService
{
    /// <summary>Validates against the guest entitlement and quota and persists the reservation in the caller's transaction. No provider call.</summary>
    Task<GuestMediaReservationOutcome> ReserveAsync(GuestMediaReservationCommand command, CancellationToken cancellationToken);

    /// <summary>Mints the provider capability after the reservation committed; null when the provider is unavailable or returned an unsafe capability.</summary>
    Task<MediaUploadCapability?> IssueCapabilityAsync(GuestMediaReservation reservation, CancellationToken cancellationToken);

    /// <summary>
    /// Closes assets of an abandoned owner in the caller's transaction (PD-16): open uploads become Rejected, Ready
    /// assets become PendingDeletion. Provider bytes stay until permanent purge and every state keeps consuming quota.
    /// </summary>
    Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes an owner's Guest assets in the caller's transaction: closes intents, transitions every
    /// non-deleted asset to PendingDeletion, and idempotently enqueues provider deletion work. The worker must wait
    /// until all issued upload intents expire before deleting provider bytes.
    /// </summary>
    Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);
}

/// <summary>Whether the configured Media provider can currently accept Guest upload capabilities.</summary>
public interface IGuestMediaUploadAvailability
{
    bool IsAvailable { get; }
}

/// <summary>Server-side verification of a Guest asset from provider evidence (never from a client callback).</summary>
public interface IGuestMediaVerificationService
{
    Task<MediaFinalizeResult> VerifyAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken);
}
