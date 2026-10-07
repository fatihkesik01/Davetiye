using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

public sealed class AbandonedMemoryRetentionSettingsReader(DavetiyeDbContext dbContext)
    : IAbandonedMemoryRetentionSettingsReader
{
    public async Task<int?> ReadDaysAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.Key == "abandonedMemoryRetentionDays", cancellationToken);
        return setting is null ? null : SystemSettingRetentionValues.ReadDays(setting.ValueType.ToString(), setting.Value);
    }
}
