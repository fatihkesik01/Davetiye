namespace Davetiye.Domain.Modules.Memories;

/// <summary>
/// One guest memory (text/emoji/optional media). Visibility is deliberately not stored here: it is
/// a per-invitation <see cref="MemoryConfiguration"/> setting so toggling it never rewrites memories.
/// </summary>
public sealed class Memory
{
    private readonly List<MemoryMedia> _media = [];

    private Memory() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Text { get; private set; }
    public string? Emoji { get; private set; }
    public MemoryState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FinalizedAt { get; private set; }
    public DateTimeOffset? HiddenAt { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<MemoryMedia> Media => _media.AsReadOnly();

    /// <summary>
    /// Creates a memory. Without media it is Published at once and needs text or emoji; with media
    /// it starts PendingMedia and becomes Published only through <see cref="Finalize"/>.
    /// </summary>
    public static Memory Create(Guid id, Guid invitationId, string? displayName, string? text, string? emoji,
        bool expectsMedia, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Memory and invitation identifiers must not be empty.");
        MemoryTime.EnsureUtc(createdAt, nameof(createdAt));
        var name = Normalize(displayName);
        var body = Normalize(text);
        var symbol = Normalize(emoji);
        if (name is not null && name.Length > MemoryInputLimits.HardMaxDisplayNameCharacters)
            throw new ArgumentException("Display name exceeds the hard ceiling.", nameof(displayName));
        if (body is not null && body.Length > MemoryInputLimits.HardMaxTextCharacters)
            throw new ArgumentException("Text exceeds the hard ceiling.", nameof(text));
        if (symbol is not null && symbol.Length > MemoryInputLimits.HardMaxEmojiCharacters)
            throw new ArgumentException("Emoji exceeds the hard ceiling.", nameof(emoji));
        if (!expectsMedia && body is null && symbol is null)
            throw new ArgumentException("A memory without media needs text or an emoji.");
        return new Memory
        {
            Id = id,
            InvitationId = invitationId,
            DisplayName = name,
            Text = body,
            Emoji = symbol,
            State = expectsMedia ? MemoryState.PendingMedia : MemoryState.Published,
            CreatedAt = createdAt,
            FinalizedAt = expectsMedia ? null : createdAt
        };
    }

    public MemoryMedia AttachMedia(Guid mediaAssetId, Guid linkId)
    {
        if (State != MemoryState.PendingMedia)
            throw new InvalidOperationException("Media can only be attached while the memory is pending media.");
        if (_media.Count >= MemoryInputLimits.HardMaxMediaPerMemory)
            throw new InvalidOperationException("Memory media count exceeds the hard ceiling.");
        if (_media.Any(item => item.MediaAssetId == mediaAssetId))
            throw new InvalidOperationException("Media asset is already attached to this memory.");
        var link = MemoryMedia.Create(linkId, Id, mediaAssetId, _media.Count);
        _media.Add(link);
        Revision++;
        return link;
    }

    /// <summary>Removes the link to a rejected asset while pending; the Media-owned asset itself is never touched here.</summary>
    public void DropMedia(Guid mediaAssetId)
    {
        if (State != MemoryState.PendingMedia)
            throw new InvalidOperationException("Media can only be dropped while the memory is pending media.");
        var link = _media.SingleOrDefault(item => item.MediaAssetId == mediaAssetId)
            ?? throw new InvalidOperationException("Media asset is not attached to this memory.");
        _media.Remove(link);
        Revision++;
    }

    public void Finalize(DateTimeOffset finalizedAt)
    {
        MemoryTime.EnsureUtc(finalizedAt, nameof(finalizedAt));
        if (State != MemoryState.PendingMedia)
            throw new InvalidOperationException("Only a pending-media memory can be finalized.");
        // After rejected media are dropped a memory may legitimately keep only its text/emoji; an empty memory never finalizes.
        if (_media.Count == 0 && Text is null && Emoji is null)
            throw new InvalidOperationException("A pending-media memory needs at least one media item or text/emoji.");
        if (finalizedAt < CreatedAt) throw new ArgumentException("Memory timestamps must be monotonic.", nameof(finalizedAt));
        State = MemoryState.Published;
        FinalizedAt = finalizedAt;
        Revision++;
    }

    public void Abandon()
    {
        if (State != MemoryState.PendingMedia)
            throw new InvalidOperationException("Only a pending-media memory can be abandoned.");
        State = MemoryState.Abandoned;
        Revision++;
    }

    public void Hide(DateTimeOffset hiddenAt)
    {
        MemoryTime.EnsureUtc(hiddenAt, nameof(hiddenAt));
        if (State == MemoryState.Hidden) return;
        if (State != MemoryState.Published)
            throw new InvalidOperationException("Only a published memory can be hidden.");
        if (hiddenAt < FinalizedAt) throw new ArgumentException("Memory timestamps must be monotonic.", nameof(hiddenAt));
        State = MemoryState.Hidden;
        HiddenAt = hiddenAt;
        Revision++;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
