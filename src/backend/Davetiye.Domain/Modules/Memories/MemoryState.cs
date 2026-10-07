namespace Davetiye.Domain.Modules.Memories;

public enum MemoryState
{
    /// <summary>Created with media still to be uploaded/finalized; never shown to anyone but internal workflows.</summary>
    PendingMedia,
    /// <summary>Accepted. Public exposure is decided by <see cref="MemoryProjectionPolicy"/>, not by this state alone.</summary>
    Published,
    /// <summary>Hidden by the Creator. Data is retained until the Creator deletes it.</summary>
    Hidden,
    /// <summary>A pending-media memory whose upload window elapsed without finalize.</summary>
    Abandoned
}
