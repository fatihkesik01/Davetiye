using System.Threading.RateLimiting;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AccountUiPreferencesRateLimiter : IAccountUiPreferencesRateLimiter, IDisposable
{
    private readonly IOptionsMonitor<AuthRateLimitOptions> _options;
    private readonly PartitionedRateLimiter<Guid> _limiter;

    public AccountUiPreferencesRateLimiter(IOptionsMonitor<AuthRateLimitOptions> options)
    {
        _options = options;
        _limiter = PartitionedRateLimiter.Create<Guid, Guid>(identityUserId =>
            RateLimitPartition.GetFixedWindowLimiter(identityUserId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = _options.CurrentValue.UiPreferencesWrite.PermitLimit,
                Window = TimeSpan.FromSeconds(_options.CurrentValue.UiPreferencesWrite.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public bool TryAcquire(Guid identityUserId)
    {
        if (identityUserId == Guid.Empty)
            return false;

        using var lease = _limiter.AttemptAcquire(identityUserId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
