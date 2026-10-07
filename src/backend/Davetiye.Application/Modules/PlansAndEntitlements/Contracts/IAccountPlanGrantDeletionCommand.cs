namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>Plans-owned command to revoke an account's active grants as part of verified deletion.</summary>
public interface IAccountPlanGrantDeletionCommand
{
    Task RevokeActiveGrantsAsync(Guid accountId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken);
}
