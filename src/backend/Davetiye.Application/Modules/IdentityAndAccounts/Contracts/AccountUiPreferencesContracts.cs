namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public sealed record AccountUiPreferences(string Locale, string ColorTheme, string Appearance, string? Avatar);

public interface IAccountUiPreferencesService
{
    Task<AccountUiPreferences?> GetAsync(Guid identityUserId, CancellationToken cancellationToken);

    Task<AccountUiPreferences?> UpdateAsync(
        Guid identityUserId,
        AccountUiPreferences preferences,
        CancellationToken cancellationToken);
}

public interface IAccountUiPreferencesRateLimiter
{
    bool TryAcquire(Guid identityUserId);
}
