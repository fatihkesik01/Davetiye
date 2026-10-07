using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AccountConsentService(DavetiyeDbContext dbContext, Davetiye.Domain.Modules.SharedKernel.IClock clock)
    : IAccountConsentService
{
    public async Task<bool> IsServiceNoticeAcknowledgedAsync(Guid identityUserId, CancellationToken cancellationToken)
    {
        if (identityUserId == Guid.Empty)
            return false;

        return await dbContext.Accounts.AsNoTracking()
            .Where(account => account.IdentityUserId == identityUserId)
            .Join(dbContext.AccountConsentRecords,
                account => account.Id,
                record => record.AccountId,
                (_, record) => record)
            .AnyAsync(record => record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement && record.Granted &&
                record.Version == AccountConsentVersions.ServiceNotice,
                cancellationToken);
    }

    public async Task<AccountConsentSnapshot?> AcknowledgeServiceNoticeAsync(
        Guid identityUserId,
        bool acknowledged,
        CancellationToken cancellationToken)
    {
        if (!acknowledged || identityUserId == Guid.Empty)
            return null;

        var accountId = await dbContext.Accounts.AsNoTracking()
            .Where(account => account.IdentityUserId == identityUserId)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (accountId is null)
            return null;

        var exists = await dbContext.AccountConsentRecords.AnyAsync(record =>
            record.AccountId == accountId && record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement &&
            record.Granted && record.Version == AccountConsentVersions.ServiceNotice,
            cancellationToken);
        if (!exists)
        {
            dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
                Guid.NewGuid(), accountId.Value, AccountConsentKind.ServiceNoticeAcknowledgement, true,
                AccountConsentVersions.ServiceNotice, AccountConsentSource.ExistingAccountAcknowledgement, clock.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await LoadSnapshotAsync(accountId.Value, cancellationToken);
    }

    public async Task<AccountConsentSnapshot?> GetAsync(Guid identityUserId, CancellationToken cancellationToken)
    {
        if (identityUserId == Guid.Empty)
            return null;

        var accountId = await dbContext.Accounts.AsNoTracking()
            .Where(account => account.IdentityUserId == identityUserId)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return accountId is null ? null : await LoadSnapshotAsync(accountId.Value, cancellationToken);
    }

    public async Task<AccountConsentSnapshot?> UpdateMarketingPreferenceAsync(
        Guid identityUserId,
        bool optedIn,
        CancellationToken cancellationToken)
    {
        if (identityUserId == Guid.Empty)
            return null;

        var accountId = await dbContext.Accounts.AsNoTracking()
            .Where(account => account.IdentityUserId == identityUserId)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (accountId is null)
            return null;

        var latest = await dbContext.AccountConsentRecords
            .Where(record => record.AccountId == accountId && record.Kind == AccountConsentKind.MarketingPreference)
            .OrderByDescending(record => record.RecordedAt)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Repeating the current preference is idempotent and does not create misleading audit events.
        if (latest?.Granted != optedIn)
        {
            dbContext.AccountConsentRecords.Add(AccountConsentRecord.Create(
                Guid.NewGuid(),
                accountId.Value,
                AccountConsentKind.MarketingPreference,
                optedIn,
                AccountConsentVersions.MarketingPreference,
                AccountConsentSource.AccountSettings,
                clock.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await LoadSnapshotAsync(accountId.Value, cancellationToken);
    }

    private async Task<AccountConsentSnapshot> LoadSnapshotAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var records = await dbContext.AccountConsentRecords.AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .OrderByDescending(record => record.RecordedAt)
            .ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken);

        var serviceNotice = records.FirstOrDefault(record => record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement);
        var marketing = records.FirstOrDefault(record => record.Kind == AccountConsentKind.MarketingPreference);

        return new AccountConsentSnapshot(
            new ServiceNoticeConsentSnapshot(
                serviceNotice is not null && serviceNotice.Granted && serviceNotice.Version == AccountConsentVersions.ServiceNotice,
                serviceNotice is not null && serviceNotice.Granted && serviceNotice.Version == AccountConsentVersions.ServiceNotice
                    ? serviceNotice.RecordedAt
                    : null,
                AccountConsentVersions.ServiceNotice,
                "draft-pending-phase11-review"),
            new MarketingConsentSnapshot(
                marketing?.Granted ?? false,
                marketing?.RecordedAt,
                marketing?.Version ?? AccountConsentVersions.MarketingPreference),
            records.Select(record => new ConsentHistoryItem(
                record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement
                    ? "serviceNoticeAcknowledgement"
                    : "marketingPreference",
                record.Granted,
                record.Version,
                record.Source switch
                {
                    AccountConsentSource.EmailPasswordSignup => "emailPasswordSignup",
                    AccountConsentSource.GoogleSignup => "googleSignup",
                    AccountConsentSource.AccountSettings => "accountSettings",
                    AccountConsentSource.ExistingAccountAcknowledgement => "existingAccountAcknowledgement",
                    _ => "unknown",
                },
                record.RecordedAt)).ToArray());
    }
}
