using Davetiye.Domain.Modules.Administration;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

public sealed class InvitationRetentionSettingsInitializer(DavetiyeDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('davetiye:invitation-retention-seed', 0))", cancellationToken);
        var setting = await dbContext.SystemSettings.SingleOrDefaultAsync(
            candidate => candidate.Key == "deletedInvitationRetentionDays", cancellationToken);
        if (setting is null)
        {
            dbContext.SystemSettings.Add(SystemSetting.Create(
                new Guid("618058ba-c769-4cc0-a522-d1df1dc3a5bd"), "deletedInvitationRetentionDays",
                SystemSettingValueType.Integer, "3"));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (SystemSettingRetentionValues.ReadDays(setting.ValueType.ToString(), setting.Value) is null)
        {
            throw new InvalidOperationException("Invitation retention setting is invalid; deployment cannot supply a silent fallback.");
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
