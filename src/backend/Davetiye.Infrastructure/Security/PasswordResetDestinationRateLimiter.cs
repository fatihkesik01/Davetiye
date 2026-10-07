using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Process-local destination limiter backed by a fixed-size HMAC-indexed array. Emails never become
/// stored keys or logs. Hash collisions conservatively share a bucket; saturation cannot disable the
/// destination limit or grow memory, and each operation is O(1) without table scans.
/// </summary>
public sealed class PasswordResetDestinationRateLimiter : IPasswordResetDestinationRateLimiter
{
    private readonly object gate = new();
    private readonly Bucket[] buckets;
    private readonly byte[] hmacKey = RandomNumberGenerator.GetBytes(32);
    private readonly IOptionsMonitor<AuthRateLimitOptions> options;
    private readonly IClock clock;

    public PasswordResetDestinationRateLimiter(IOptionsMonitor<AuthRateLimitOptions> options, IClock clock)
    {
        this.options = options;
        this.clock = clock;
        // The array has fixed capacity for this process lifetime. A configuration change to bucket
        // capacity takes effect after restart; limit/window values remain monitor-backed.
        var bucketCount = options.CurrentValue.PasswordResetDestinationBucketCount;
        buckets = new Bucket[Math.Clamp(bucketCount, 1024, 1_048_576)];
    }

    internal int BucketCount => buckets.Length;

    public bool TryAcquire(string? email)
    {
        var limits = options.CurrentValue;
        var now = clock.UtcNow;
        var digest = HashDestination(email);
        var index = (int)(BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(0, sizeof(uint))) % (uint)buckets.Length);
        var permitLimit = limits.PasswordResetRequestDestination.PermitLimit;
        var windowTicks = TimeSpan.FromSeconds(limits.PasswordResetRequestDestination.WindowSeconds).Ticks;
        var nowTicks = now.UtcDateTime.Ticks;

        lock (gate)
        {
            ref var bucket = ref buckets[index];
            if (!bucket.Occupied || nowTicks >= bucket.ExpiresAtUtcTicks)
            {
                bucket = new Bucket(1, checked(nowTicks + windowTicks), occupied: true);
                return true;
            }

            if (bucket.Count >= permitLimit)
                return false;

            bucket.Count++;
            return true;
        }
    }

    private byte[] HashDestination(string? email)
    {
        var normalized = string.IsNullOrWhiteSpace(email)
            ? "<missing>"
            : email.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(normalized));
    }

    private struct Bucket(int count, long expiresAtUtcTicks, bool occupied)
    {
        public int Count = count;
        public long ExpiresAtUtcTicks = expiresAtUtcTicks;
        public bool Occupied = occupied;
    }
}
