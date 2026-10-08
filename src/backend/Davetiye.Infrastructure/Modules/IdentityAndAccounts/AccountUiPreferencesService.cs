using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public sealed class AccountUiPreferencesService(DavetiyeDbContext dbContext) : IAccountUiPreferencesService
{
    public async Task<AccountUiPreferences?> GetAsync(Guid identityUserId, CancellationToken cancellationToken)
    {
        if (identityUserId == Guid.Empty)
            return null;

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == identityUserId, cancellationToken);
        return user is null
            ? null
            : new AccountUiPreferences(user.PreferredLocale, user.PreferredColorTheme, user.PreferredAppearance);
    }

    public async Task<AccountUiPreferences?> UpdateAsync(
        Guid identityUserId,
        AccountUiPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (identityUserId == Guid.Empty)
            return null;

        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == identityUserId, cancellationToken);
        if (user is null)
            return null;

        user.UpdateUiPreferences(preferences.Locale, preferences.ColorTheme, preferences.Appearance);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AccountUiPreferences(user.PreferredLocale, user.PreferredColorTheme, user.PreferredAppearance);
    }
}
