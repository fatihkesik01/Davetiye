namespace Davetiye.Application.Modules.Media.Contracts;

/// <summary>
/// Provider-neutral kind exposed across the Media boundary so that Memories never references Media domain types.
/// </summary>
public enum GuestMediaKind
{
    Image,
    Video
}

public enum GuestMediaAssetReadiness
{
    Pending,
    Ready,
    Rejected,
    Deleted
}

/// <summary>Narrow, read-only view of a Guest-scoped asset; exposes no provider reference or delivery URL.</summary>
public sealed record GuestMediaAssetStatus(
    Guid AssetId,
    Guid InvitationId,
    GuestMediaKind Kind,
    GuestMediaAssetReadiness Readiness);

/// <summary>
/// Port Memories uses instead of reading Media tables (implemented by Media since P6-M3). Assets that are not
/// Guest-scoped or do not belong to the given invitation are omitted from the result. PendingUpload and
/// Processing map to Pending; PendingDeletion and Deleted map to Deleted.
/// </summary>
public interface IGuestMediaAssetStatusReader
{
    Task<IReadOnlyList<GuestMediaAssetStatus>> ListAsync(
        Guid invitationId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken);
}
