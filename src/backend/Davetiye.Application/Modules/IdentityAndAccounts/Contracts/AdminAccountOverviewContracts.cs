namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public sealed record AdminAccountOverview(long Total, long Individual, long Organization, long Banned);

public interface IAdminAccountOverviewReader
{
    Task<AdminAccountOverview> GetAsync(CancellationToken cancellationToken);
}
