using Davetiye.Domain.Modules.Administration;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

/// <summary>Seeds the 30-day product starter value without overwriting operator changes.</summary>
public sealed class AbandonedMemoryRetentionSettingsInitializer(DavetiyeDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('davetiye:abandoned-memory-retention-seed', 0))", cancellationToken);
        var setting = await dbContext.SystemSettings.SingleOrDefaultAsync(
            item => item.Key == "abandonedMemoryRetentionDays", cancellationToken);
        if (setting is null)
        {
            dbContext.SystemSettings.Add(SystemSetting.Create(
                new Guid("c45377c2-11c0-4ca0-9ff4-2a8449804657"), "abandonedMemoryRetentionDays",
                SystemSettingValueType.Integer, "30"));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (SystemSettingRetentionValues.ReadDays(setting.ValueType.ToString(), setting.Value) is null)
        {
            throw new InvalidOperationException("Abandoned memory retention setting is invalid; no fallback is applied.");
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
