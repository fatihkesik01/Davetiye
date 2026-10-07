using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AccountReferenceValidator(DavetiyeDbContext dbContext) : IAccountReferenceValidator
{
    public async Task<AccountReferenceStatus> GetStatusAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountReference = await (
                from account in dbContext.Accounts.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on account.IdentityUserId equals user.Id
                where account.Id == accountId
                select new
                {
                    user.EmailConfirmed,
                    account.DeletionStartedAtUtc,
                    IsBanned = dbContext.BanRecords
                        .Any(ban => ban.AccountId == account.Id && ban.RevokedAt == null)
                })
            .SingleOrDefaultAsync(cancellationToken);

        return accountReference switch
        {
            null => AccountReferenceStatus.NotFound,
            { DeletionStartedAtUtc: not null } => AccountReferenceStatus.Deleting,
            { IsBanned: true } => AccountReferenceStatus.Banned,
            { EmailConfirmed: true } => AccountReferenceStatus.Verified,
            _ => AccountReferenceStatus.Unverified
        };
    }
}
