using System.Threading.RateLimiting;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>Limits Creator Memories requests per resolved account with independent read and write windows.</summary>
public sealed class CreatorMemoriesRateLimiter : ICreatorMemoriesRateLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _reads;
    private readonly PartitionedRateLimiter<string> _writes;

    public CreatorMemoriesRateLimiter(IOptions<AuthRateLimitOptions> options)
    {
        var limits = options.Value;
        _reads = Create(limits.CreatorMemoriesReadAccount);
        _writes = Create(limits.CreatorMemoriesWriteAccount);
    }

    public bool TryAcquire(Guid accountId, CreatorMemoriesRateLimitBucket bucket)
    {
        if (accountId == Guid.Empty) return false;
        using var lease = (bucket == CreatorMemoriesRateLimitBucket.Read ? _reads : _writes)
            .AttemptAcquire(accountId.ToString("N"));
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _reads.Dispose();
        _writes.Dispose();
    }

    private static PartitionedRateLimiter<string> Create(AuthRateLimitOptions.RouteRateLimit limit) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
}
