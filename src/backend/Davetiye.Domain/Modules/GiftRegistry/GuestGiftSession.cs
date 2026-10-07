namespace Davetiye.Domain.Modules.GiftRegistry;

/// <summary>Invitation-scoped guest capability. Only the purpose-scoped HMAC digest is persisted.</summary>
public sealed class GuestGiftSession
{
    private byte[] _hmacDigest = [];

    public const string RequiredPurpose = "gift-session";
    public const int HmacSha256DigestLength = 32;

    private GuestGiftSession() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public string Purpose { get; private set; } = RequiredPurpose;
    public int HmacKeyVersion { get; private set; }
    public byte[] HmacDigest
    {
        get => _hmacDigest.ToArray();
        private set => _hmacDigest = value.ToArray();
    }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static GuestGiftSession Create(Guid id, Guid invitationId, string purpose, int hmacKeyVersion,
        byte[] hmacDigest, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Gift session and invitation identifiers must not be empty.");
        if (!string.Equals(purpose, RequiredPurpose, StringComparison.Ordinal))
            throw new ArgumentException("Gift session purpose is fixed.", nameof(purpose));
        if (hmacKeyVersion <= 0) throw new ArgumentOutOfRangeException(nameof(hmacKeyVersion));
        ArgumentNullException.ThrowIfNull(hmacDigest);
        if (hmacDigest.Length != HmacSha256DigestLength)
            throw new ArgumentException("Gift session must contain a SHA-256 HMAC digest only.", nameof(hmacDigest));
        if (createdAt.Offset != TimeSpan.Zero) throw new ArgumentException("Gift timestamps must be UTC.", nameof(createdAt));
        return new GuestGiftSession
        {
            Id = id,
            InvitationId = invitationId,
            Purpose = RequiredPurpose,
            HmacKeyVersion = hmacKeyVersion,
            HmacDigest = hmacDigest,
            CreatedAt = createdAt
        };
    }

    /// <summary>Ends management access after the session's final active reservation is removed.</summary>
    public void Revoke(DateTimeOffset revokedAt)
    {
        if (revokedAt.Offset != TimeSpan.Zero) throw new ArgumentException("Gift timestamps must be UTC.", nameof(revokedAt));
        if (revokedAt < CreatedAt) throw new ArgumentException("Gift timestamps must be monotonic.", nameof(revokedAt));
        RevokedAt ??= revokedAt;
    }
}
