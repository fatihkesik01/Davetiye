using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

public sealed class InvitationRetentionSettingsReader(DavetiyeDbContext dbContext) : IInvitationRetentionSettingsReader
{
    public async Task<int?> ReadDaysAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Key == "deletedInvitationRetentionDays", cancellationToken);
        return setting is null ? null : SystemSettingRetentionValues.ReadDays(setting.ValueType.ToString(), setting.Value);
    }
}
