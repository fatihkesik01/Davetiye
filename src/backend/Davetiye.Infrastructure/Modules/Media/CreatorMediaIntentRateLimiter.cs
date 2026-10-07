using System.Threading.RateLimiting;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Applies the configurable second rate-limit dimension after the principal resolves to an Account.</summary>
public sealed class CreatorMediaIntentRateLimiter : ICreatorMediaIntentRateLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public CreatorMediaIntentRateLimiter(IOptions<AuthRateLimitOptions> options)
    {
        var limit = options.Value.CreatorMediaIntentAccount;
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public bool TryAcquire(Guid accountId, string clientIpAddress)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(clientIpAddress))
        {
            return false;
        }

        // The endpoint's ASP.NET Core policy enforces per-IP limits. This separate partition
        // deliberately keys only by Account so changing networks cannot reset an account's quota.
        using var lease = _limiter.AttemptAcquire(accountId.ToString("N"));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
