namespace Davetiye.Domain.Modules.Memories;

/// <summary>
/// Pure rules separating the Public projection from the Creator projection. Callers supply the
/// entitlement and invitation-effective-state facts; this type owns no cross-module lookups.
/// </summary>
public static class MemoryProjectionPolicy
{
    /// <summary>New submissions need the module on, the entitlement on, and an effective-Active invitation (never Scheduled).</summary>
    public static bool CanAcceptSubmission(bool moduleEnabled, bool memoriesEntitled, bool invitationEffectivelyActive) =>
        moduleEnabled && memoriesEntitled && invitationEffectivelyActive;

    /// <summary>
    /// Public listing: Published memories, and PendingMedia memories while their text or Ready media
    /// is projected, when the module is enabled, visibility is Public and entitlement is on.
    /// </summary>
    public static bool IsVisibleToPublic(MemoryState state, bool moduleEnabled, MemoryVisibility visibility,
        bool memoriesEntitled) =>
        state is MemoryState.Published or MemoryState.PendingMedia && moduleEnabled && memoriesEntitled && visibility == MemoryVisibility.Public;

    /// <summary>Creator list: accepted memories, including Hidden ones and regardless of entitlement or visibility.</summary>
    public static bool IsVisibleToCreator(MemoryState state) =>
        state is MemoryState.Published or MemoryState.Hidden;

    /// <summary>A media item is shown only once the Media module reports it Ready.</summary>
    public static bool IsMediaVisible(bool mediaReady) => mediaReady;
}
