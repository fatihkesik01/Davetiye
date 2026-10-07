using System.Threading.RateLimiting;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Rsvp;

/// <summary>Limits Creator RSVP requests per resolved account with independent read and write windows.</summary>
public sealed class CreatorRsvpRateLimiter : ICreatorRsvpRateLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _reads;
    private readonly PartitionedRateLimiter<string> _writes;

    public CreatorRsvpRateLimiter(IOptions<AuthRateLimitOptions> options)
    {
        var limits = options.Value;
        _reads = Create(limits.CreatorRsvpReadAccount);
        _writes = Create(limits.CreatorRsvpWriteAccount);
    }

    public bool TryAcquire(Guid accountId, CreatorRsvpRateLimitBucket bucket)
    {
        if (accountId == Guid.Empty) return false;
        using var lease = (bucket == CreatorRsvpRateLimitBucket.Read ? _reads : _writes)
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
