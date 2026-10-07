namespace Davetiye.Domain.Modules.Memories;

/// <summary>
/// Short-lived, single-memory upload capability. Stores only a purpose-scoped HMAC digest, never
/// the guest token. At most one unconsumed, unrevoked capability exists per memory.
/// </summary>
public sealed class MemoryUploadCapability
{
    private byte[] _hmacDigest = [];

    public const string RequiredPurpose = "memory-upload";
    public const int HmacSha256DigestLength = 32;

    private MemoryUploadCapability() { }

    public Guid Id { get; private set; }
    public Guid MemoryId { get; private set; }
    public string Purpose { get; private set; } = RequiredPurpose;
    public int HmacKeyVersion { get; private set; }
    public byte[] HmacDigest
    {
        get => _hmacDigest.ToArray();
        private set => _hmacDigest = value.ToArray();
    }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static MemoryUploadCapability Create(Guid id, Guid memoryId, string purpose, int hmacKeyVersion,
        byte[] hmacDigest, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || memoryId == Guid.Empty)
            throw new ArgumentException("Capability and memory identifiers must not be empty.");
        if (!string.Equals(purpose, RequiredPurpose, StringComparison.Ordinal))
            throw new ArgumentException("Memory upload capability purpose is fixed.", nameof(purpose));
        if (hmacKeyVersion <= 0) throw new ArgumentOutOfRangeException(nameof(hmacKeyVersion));
        ArgumentNullException.ThrowIfNull(hmacDigest);
        if (hmacDigest.Length != HmacSha256DigestLength)
            throw new ArgumentException("Capability must contain a SHA-256 HMAC digest only.", nameof(hmacDigest));
        MemoryTime.EnsureUtc(createdAt, nameof(createdAt));
        MemoryTime.EnsureUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt ||
            expiresAt - createdAt > TimeSpan.FromMinutes(MemoryInputLimits.HardMaxUploadCapabilityLifetimeMinutes))
            throw new ArgumentException(
                $"Capability expiry must be after creation and within {MemoryInputLimits.HardMaxUploadCapabilityLifetimeMinutes} minutes.",
                nameof(expiresAt));
        return new MemoryUploadCapability
        {
            Id = id,
            MemoryId = memoryId,
            Purpose = RequiredPurpose,
            HmacKeyVersion = hmacKeyVersion,
            HmacDigest = hmacDigest.ToArray(),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public bool IsUsableAt(DateTimeOffset at) => ConsumedAt is null && RevokedAt is null && at < ExpiresAt;

    public void Consume(DateTimeOffset consumedAt)
    {
        MemoryTime.EnsureUtc(consumedAt, nameof(consumedAt));
        if (!IsUsableAt(consumedAt)) throw new InvalidOperationException("Capability is not usable.");
        if (consumedAt < CreatedAt) throw new ArgumentException("Memory timestamps must be monotonic.", nameof(consumedAt));
        ConsumedAt = consumedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        MemoryTime.EnsureUtc(revokedAt, nameof(revokedAt));
        if (revokedAt < CreatedAt) throw new ArgumentException("Memory timestamps must be monotonic.", nameof(revokedAt));
        RevokedAt ??= revokedAt;
    }
}
