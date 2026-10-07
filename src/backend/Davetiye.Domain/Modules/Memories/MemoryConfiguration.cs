namespace Davetiye.Domain.Modules.Memories;

/// <summary>Memories-owned per-invitation settings. Invitation is referenced by ID only. Disabled and CreatorOnly by default.</summary>
public sealed class MemoryConfiguration
{
    private MemoryConfiguration() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public bool IsEnabled { get; private set; }
    public MemoryVisibility Visibility { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Revision { get; private set; }

    public static MemoryConfiguration Create(Guid id, Guid invitationId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Configuration and invitation identifiers must not be empty.");
        MemoryTime.EnsureUtc(createdAt, nameof(createdAt));
        return new MemoryConfiguration
        {
            Id = id,
            InvitationId = invitationId,
            IsEnabled = false,
            Visibility = MemoryVisibility.CreatorOnly,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void SetEnabled(bool enabled, DateTimeOffset changedAt)
    {
        Validate(changedAt);
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        Touch(changedAt);
    }

    /// <summary>Changes visibility for every memory of the invitation at once; memory rows are never rewritten.</summary>
    public void SetVisibility(MemoryVisibility visibility, DateTimeOffset changedAt)
    {
        if (!Enum.IsDefined(visibility)) throw new ArgumentOutOfRangeException(nameof(visibility));
        Validate(changedAt);
        if (Visibility == visibility) return;
        Visibility = visibility;
        Touch(changedAt);
    }

    private void Validate(DateTimeOffset changedAt)
    {
        MemoryTime.EnsureUtc(changedAt, nameof(changedAt));
        if (changedAt < UpdatedAt) throw new ArgumentException("Memory timestamps must be monotonic.", nameof(changedAt));
    }

    private void Touch(DateTimeOffset at)
    {
        UpdatedAt = at;
        Revision++;
    }
}
