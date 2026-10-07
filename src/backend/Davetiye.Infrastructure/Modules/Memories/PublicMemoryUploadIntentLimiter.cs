using System.Threading.RateLimiting;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>
/// Fixed-window limit per invitation for anonymous guest upload intents. Keyed by the internal invitation id and
/// consulted only after the upload capability validated, so it cannot be used to probe memories or public codes.
/// </summary>
public sealed class PublicMemoryUploadIntentLimiter : IPublicMemoryUploadIntentLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<Guid> limiter;

    public PublicMemoryUploadIntentLimiter(IOptions<AuthRateLimitOptions> options)
    {
        var limit = options.Value.PublicMemoryUploadIntentPerInvitation;
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
