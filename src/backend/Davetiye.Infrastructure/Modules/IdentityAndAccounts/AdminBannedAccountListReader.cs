using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AdminBannedAccountListReader(DavetiyeDbContext db) : IAdminBannedAccountListReader
{
    public async Task<AdminBannedAccountPage> GetPageAsync(
        int page,
        int pageSize,
        string? emailPrefix,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page is outside the supported range.");

        var query = from ban in db.BanRecords.AsNoTracking()
                    join account in db.Accounts.AsNoTracking() on ban.AccountId equals account.Id
                    join user in db.Users.AsNoTracking() on account.IdentityUserId equals user.Id
                    where ban.RevokedAt == null
                    select new { Ban = ban, Account = account, User = user };

        if (!string.IsNullOrEmpty(emailPrefix))
        {
            var normalizedPrefix = emailPrefix.ToLowerInvariant();
            query = query.Where(row => row.User.Email != null
                && row.User.Email.ToLower().StartsWith(normalizedPrefix));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(row => row.Ban.BannedAt)
            .ThenByDescending(row => row.Account.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new AdminBannedAccountListItem(
                row.Account.Id,
                row.Account.DisplayName,
                row.Account.AccountType == AccountType.Individual
                    ? AdminBannedAccountType.Individual
                    : AdminBannedAccountType.Organization,
                row.User.Email,
                row.Account.CreatedAt,
                row.Ban.BannedAt,
                row.Ban.Reason))
            .ToListAsync(cancellationToken);

        return new AdminBannedAccountPage(page, pageSize, totalCount, items);
    }
}
