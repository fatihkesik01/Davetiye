namespace Davetiye.Domain.Modules.Rsvp;

/// <summary>Stores only a purpose- and submission-scoped HMAC digest, never the guest token.</summary>
public sealed class RsvpManageCapability
{
    private byte[] _hmacDigest = [];

    public const string RequiredPurpose = "rsvp-manage";
    public const int HmacSha256DigestLength = 32;

    private RsvpManageCapability() { }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public string Purpose { get; private set; } = RequiredPurpose;
    public int HmacKeyVersion { get; private set; }
    public byte[] HmacDigest
    {
        get => _hmacDigest.ToArray();
        private set => _hmacDigest = value.ToArray();
    }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RsvpManageCapability Create(Guid id, Guid submissionId, string purpose, int hmacKeyVersion,
        byte[] hmacDigest, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || submissionId == Guid.Empty)
            throw new ArgumentException("Capability and submission identifiers must not be empty.");
        if (!string.Equals(purpose, RequiredPurpose, StringComparison.Ordinal))
            throw new ArgumentException("RSVP capability purpose is fixed.", nameof(purpose));
        if (hmacKeyVersion <= 0) throw new ArgumentOutOfRangeException(nameof(hmacKeyVersion));
        ArgumentNullException.ThrowIfNull(hmacDigest);
        if (hmacDigest.Length != HmacSha256DigestLength)
            throw new ArgumentException("Capability must contain a SHA-256 HMAC digest only.", nameof(hmacDigest));
        if (createdAt.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", nameof(createdAt));
        if (expiresAt.Offset != TimeSpan.Zero || expiresAt <= createdAt)
            throw new ArgumentException("Capability expiry must be a UTC time strictly after creation.", nameof(expiresAt));
        return new RsvpManageCapability
        {
            Id = id,
            SubmissionId = submissionId,
            Purpose = RequiredPurpose,
            HmacKeyVersion = hmacKeyVersion,
            HmacDigest = hmacDigest.ToArray(),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (revokedAt.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", nameof(revokedAt));
        if (revokedAt < CreatedAt) throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(revokedAt));
        RevokedAt ??= revokedAt;
    }
}
