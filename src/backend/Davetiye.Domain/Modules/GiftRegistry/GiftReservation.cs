namespace Davetiye.Domain.Modules.GiftRegistry;

/// <summary>Active guest reservation; deleting it on cancel/removal also deletes its contact data.</summary>
public sealed class GiftReservation
{
    private GiftReservation() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public Guid GiftItemId { get; private set; }
    public Guid GuestGiftSessionId { get; private set; }
    public int Quantity { get; private set; }
    public string GuestFullName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static GiftReservation Create(Guid id, Guid invitationId, Guid giftItemId, Guid guestGiftSessionId,
        int quantity, string guestFullName, string? email, string? phone, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty || giftItemId == Guid.Empty || guestGiftSessionId == Guid.Empty)
            throw new ArgumentException("Gift reservation identifiers must not be empty.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var name = NormalizeRequired(guestFullName, 200, nameof(guestFullName));
        var normalizedEmail = NormalizeOptional(email, 320, nameof(email));
        var normalizedPhone = NormalizeOptional(phone, 32, nameof(phone));
        if (normalizedEmail is not null && normalizedEmail.Length < 3)
            throw new ArgumentException("Email is too short.", nameof(email));
        if (createdAt.Offset != TimeSpan.Zero) throw new ArgumentException("Gift timestamps must be UTC.", nameof(createdAt));
        return new GiftReservation
        {
            Id = id,
            InvitationId = invitationId,
            GiftItemId = giftItemId,
            GuestGiftSessionId = guestGiftSessionId,
            Quantity = quantity,
            GuestFullName = name,
            Email = normalizedEmail,
            Phone = normalizedPhone,
            CreatedAt = createdAt
        };
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length is < 1 || normalized.Length > maxLength)
            throw new ArgumentException($"Value must contain between 1 and {maxLength} characters.", parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Value must contain at most {maxLength} characters.", parameterName);
        return normalized;
    }
}
