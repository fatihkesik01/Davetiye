using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class PaymentAccountEligibilityReader(DavetiyeDbContext db) : IPaymentAccountEligibilityReader
{
    public Task<bool> IsIndividualCreatorAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking().AnyAsync(account => account.Id == accountId &&
            account.DeletionStartedAtUtc == null && account.AccountType == AccountType.Individual, cancellationToken);
}
