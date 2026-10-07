namespace Davetiye.Domain.Modules.GiftRegistry;

/// <summary>A Creator-managed item in one invitation's gift registry.</summary>
public sealed class GiftItem
{
    private GiftItem() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int RequestedQuantity { get; private set; }
    public int Ordinal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Revision { get; private set; }

    public static GiftItem Create(Guid id, Guid invitationId, string name, int requestedQuantity,
        int ordinal, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Gift item and invitation identifiers must not be empty.");
        var normalizedName = NormalizeName(name);
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        EnsureUtc(createdAt, nameof(createdAt));
        return new GiftItem
        {
            Id = id,
            InvitationId = invitationId,
            Name = normalizedName,
            RequestedQuantity = requestedQuantity,
            Ordinal = ordinal,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void Update(string name, int requestedQuantity, int ordinal, DateTimeOffset updatedAt)
    {
        var normalizedName = NormalizeName(name);
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        EnsureUtc(updatedAt, nameof(updatedAt));
        if (updatedAt < UpdatedAt) throw new ArgumentException("Gift item timestamps must be monotonic.", nameof(updatedAt));
        Name = normalizedName;
        RequestedQuantity = requestedQuantity;
        Ordinal = ordinal;
        UpdatedAt = updatedAt;
        Revision++;
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim();
        if (normalized.Length is < 1 or > 200)
            throw new ArgumentException("Gift item name must contain between 1 and 200 characters.", nameof(name));
        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("Gift timestamps must be UTC.", parameterName);
    }
}
