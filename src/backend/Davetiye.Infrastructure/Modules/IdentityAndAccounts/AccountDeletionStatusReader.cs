using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AccountDeletionStatusReader(DavetiyeDbContext db) : IAccountDeletionStatusReader
{
    public async Task<bool> IsDeletingAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking().Where(account => account.Id == accountId)
            .Select(account => (bool?)(account.DeletionStartedAtUtc != null))
            .SingleOrDefaultAsync(cancellationToken) ?? true;
}
