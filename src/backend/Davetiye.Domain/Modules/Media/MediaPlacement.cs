namespace Davetiye.Domain.Modules.Media;

/// <summary>
/// Mutable Creator working-selection metadata, not a public projection or published snapshot.
/// M4/M6 Publish/Update must copy these placement and asset references into the publication
/// snapshot, and public rendering must resolve only that snapshot; direct public reads of these
/// rows would bypass ADR-0003's explicit Update boundary. A single asset may have separate Cover
/// and Gallery placements; invitation-wide Cover cardinality is intentionally not encoded.
/// </summary>
public sealed class MediaPlacement
{
    private MediaPlacement()
    {
    }

    public Guid Id { get; private set; }
    public Guid MediaAssetId { get; private set; }
    public MediaPresentationRole Role { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Revision { get; private set; }

    internal static MediaPlacement Create(
        Guid id,
        Guid mediaAssetId,
        MediaPresentationRole role,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || mediaAssetId == Guid.Empty)
        {
            throw new ArgumentException("Placement and media asset identifiers must not be empty.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Placement creation time must be UTC.", nameof(createdAt));
        }

        return new MediaPlacement
        {
            Id = id,
            MediaAssetId = mediaAssetId,
            Role = role,
            SortOrder = sortOrder,
            CreatedAt = createdAt
        };
    }

    public void ChangeSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        if (SortOrder == sortOrder)
        {
            return;
        }

        SortOrder = sortOrder;
        Revision++;
    }
}
