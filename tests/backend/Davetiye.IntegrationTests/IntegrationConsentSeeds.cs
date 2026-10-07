using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;

namespace Davetiye.IntegrationTests;

internal static class IntegrationConsentSeeds
{
    public static void AddAcknowledgedServiceNotice(this DavetiyeDbContext db, Guid accountId, DateTimeOffset recordedAtUtc)
    {
        db.AccountConsentRecords.Add(AccountConsentRecord.Create(Guid.NewGuid(), accountId,
            AccountConsentKind.ServiceNoticeAcknowledgement, true, AccountConsentVersions.ServiceNotice,
            AccountConsentSource.ExistingAccountAcknowledgement, recordedAtUtc));
    }
}
