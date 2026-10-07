namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

public sealed record AdminPlanAndGrantOverview(
    long PlanTotal,
    long ActivePlans,
    long InactivePlans,
    long GrantTotal,
    long FreeGrants,
    long IndividualPurchaseGrants,
    long OrganizationSubscriptionGrants,
    long RevokedGrants);

public interface IAdminPlanAndGrantOverviewReader
{
    Task<AdminPlanAndGrantOverview> GetAsync(CancellationToken cancellationToken);
}
