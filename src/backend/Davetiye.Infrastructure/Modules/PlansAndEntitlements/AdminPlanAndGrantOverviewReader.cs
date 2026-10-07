using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class AdminPlanAndGrantOverviewReader(DavetiyeDbContext db) : IAdminPlanAndGrantOverviewReader
{
    public async Task<AdminPlanAndGrantOverview> GetAsync(CancellationToken cancellationToken)
    {
        var planGroups = await db.Plans.AsNoTracking()
            .GroupBy(item => item.IsActive)
            .Select(group => new { Active = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var grantGroups = await db.AccountPlanGrants.AsNoTracking()
            .GroupBy(item => item.Source)
            .Select(group => new
            {
                Source = group.Key,
                Count = group.LongCount(),
                Revoked = group.LongCount(item => item.RevokedAt != null)
            })
            .ToListAsync(cancellationToken);

        return new AdminPlanAndGrantOverview(
            planGroups.Sum(item => item.Count),
            planGroups.Where(item => item.Active).Sum(item => item.Count),
            planGroups.Where(item => !item.Active).Sum(item => item.Count),
            grantGroups.Sum(item => item.Count),
            CountGrants(GrantSource.Free),
            CountGrants(GrantSource.IndividualPurchase),
            CountGrants(GrantSource.OrganizationSubscription),
            grantGroups.Sum(item => item.Revoked));

        long CountGrants(GrantSource source) => grantGroups
            .Where(item => item.Source == source)
            .Sum(item => item.Count);
    }
}
