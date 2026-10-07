using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AdminAccountOverviewReader(DavetiyeDbContext db) : IAdminAccountOverviewReader
{
    public async Task<AdminAccountOverview> GetAsync(CancellationToken cancellationToken)
    {
        var groups = await db.Accounts.AsNoTracking()
            .GroupBy(item => item.AccountType)
            .Select(group => new { Type = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var counts = groups.ToDictionary(item => item.Type, item => item.Count);
        var banned = await db.BanRecords.AsNoTracking()
            .Where(item => item.RevokedAt == null)
            .Select(item => item.AccountId)
            .Distinct()
            .LongCountAsync(cancellationToken);

        return new AdminAccountOverview(
            groups.Sum(item => item.Count),
            counts.GetValueOrDefault(AccountType.Individual),
            counts.GetValueOrDefault(AccountType.Organization),
            banned);
    }
}
