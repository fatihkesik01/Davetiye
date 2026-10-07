using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class CurrentAccountResolver(DavetiyeDbContext dbContext) : ICurrentAccountResolver
{
    public async Task<Guid?> ResolveAccountIdAsync(
        Guid identityUserId,
        CancellationToken cancellationToken) =>
        await dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.IdentityUserId == identityUserId && account.DeletionStartedAtUtc == null)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
