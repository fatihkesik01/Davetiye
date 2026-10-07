using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PasswordResetDestinationRateLimiterTests
{
    [Fact]
    public void Destination_bucket_normalizes_case_and_whitespace_and_is_independent_of_source_ip()
    {
        var limiter = CreateLimiter(permitLimit: 2);

        Assert.True(limiter.TryAcquire("  Person@Example.test  "));
        Assert.True(limiter.TryAcquire("person@example.test"));
        Assert.False(limiter.TryAcquire("PERSON@example.test"));
        Assert.Equal(1024, limiter.BucketCount);
    }

    [Fact]
    public void Destination_bucket_resets_after_its_ttl()
    {
        var clock = new MutableClock();
        var limiter = CreateLimiter(permitLimit: 1, clock: clock);

        Assert.True(limiter.TryAcquire("person@example.test"));
        Assert.False(limiter.TryAcquire("person@example.test"));
        clock.UtcNow = clock.UtcNow.AddSeconds(3601);
        Assert.True(limiter.TryAcquire("person@example.test"));
    }

    [Fact]
    public void Fixed_bucket_array_stays_bounded_and_collisions_remain_rate_limited()
    {
        var limiter = CreateLimiter(permitLimit: 1);

        Assert.True(limiter.TryAcquire("primary@example.test"));
        var collisionRejected = false;
        for (var index = 0; index < 2048 && !collisionRejected; index++)
            collisionRejected = !limiter.TryAcquire($"address-{index}@example.test");

        Assert.True(collisionRejected);
        Assert.False(limiter.TryAcquire("primary@example.test"));
        Assert.Equal(1024, limiter.BucketCount);
    }

    private static PasswordResetDestinationRateLimiter CreateLimiter(
        int permitLimit,
        MutableClock? clock = null) =>
        new(new StaticOptionsMonitor<AuthRateLimitOptions>(new AuthRateLimitOptions
        {
            PasswordResetRequestDestination = new AuthRateLimitOptions.RouteRateLimit
            {
                PermitLimit = permitLimit,
                WindowSeconds = 3600,
            },
            PasswordResetDestinationBucketCount = 1024,
        }), clock ?? new MutableClock());

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-06T12:00:00Z");
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
