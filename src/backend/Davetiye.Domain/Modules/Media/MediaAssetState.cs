namespace Davetiye.Domain.Modules.Media;

/// <summary>Stored processing/deletion state, independent of any storage provider.</summary>
public enum MediaAssetState
{
    PendingUpload,
    Processing,
    Ready,
    Rejected,
    PendingDeletion,
    Deleted
}
