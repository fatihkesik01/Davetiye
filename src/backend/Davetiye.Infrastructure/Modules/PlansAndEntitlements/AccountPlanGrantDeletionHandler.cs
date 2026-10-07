using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

/// <summary>Plans &amp; Entitlements owns grant revocation state for verified account deletion.</summary>
public sealed class AccountPlanGrantDeletionHandler(DavetiyeDbContext db) : IAccountPlanGrantDeletionCommand
{
    public async Task RevokeActiveGrantsAsync(Guid accountId, DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || revokedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A valid account and UTC timestamp are required.");

        var grants = await db.AccountPlanGrants
            .Where(grant => grant.AccountId == accountId && grant.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var grant in grants)
            grant.Revoke(revokedAtUtc);
    }
}
