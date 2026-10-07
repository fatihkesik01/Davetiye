namespace Davetiye.Domain.Modules.Memories;

/// <summary>Links a memory to a Media-owned asset by ID only (no cross-module foreign key).</summary>
public sealed class MemoryMedia
{
    private MemoryMedia() { }

    public Guid Id { get; private set; }
    public Guid MemoryId { get; private set; }
    public Guid MediaAssetId { get; private set; }
    public int Ordinal { get; private set; }

    internal static MemoryMedia Create(Guid id, Guid memoryId, Guid mediaAssetId, int ordinal)
    {
        if (id == Guid.Empty || memoryId == Guid.Empty || mediaAssetId == Guid.Empty)
            throw new ArgumentException("Identifiers must not be empty.");
        if (ordinal < 0 || ordinal >= MemoryInputLimits.HardMaxMediaPerMemory)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return new MemoryMedia { Id = id, MemoryId = memoryId, MediaAssetId = mediaAssetId, Ordinal = ordinal };
    }
}
