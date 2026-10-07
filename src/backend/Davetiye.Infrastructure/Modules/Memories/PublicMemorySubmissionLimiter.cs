using System.Threading.RateLimiting;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>
/// Fixed-window limit per invitation for anonymous Memory submissions. It is keyed by the internal invitation id
/// and only consulted after the public-code gate passed, so it cannot be used to probe whether a code exists.
/// </summary>
public sealed class PublicMemorySubmissionLimiter : IPublicMemorySubmissionLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<Guid> limiter;

    public PublicMemorySubmissionLimiter(IOptions<AuthRateLimitOptions> options)
    {
        var limit = options.Value.PublicMemorySubmissionPerInvitation;
        limiter = PartitionedRateLimiter.Create<Guid, Guid>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public bool TryAcquire(Guid invitationId)
    {
        using var lease = limiter.AttemptAcquire(invitationId);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
