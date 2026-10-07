using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class EntitlementGrantReader(
    DavetiyeDbContext dbContext,
    IOrganizationSubscriptionEntitlementReader organizationSubscriptions) : IEntitlementGrantReader
{
    public async Task<EntitlementGrantSnapshot?> FindOwnedGrantAsync(
        Guid accountId,
        Guid grantId,
        CancellationToken cancellationToken)
    {
        // Keep grant, plan metadata and all entitlement values in one PostgreSQL statement. A
        // statement sees one MVCC snapshot; splitting this into two awaited queries can combine a
        // pre-change grant with post-change entitlement values (or vice versa).
        var grant = await (
                from candidate in dbContext.AccountPlanGrants.AsNoTracking()
                join plan in dbContext.Plans.AsNoTracking() on candidate.PlanId equals plan.Id
                where candidate.Id == grantId && candidate.AccountId == accountId
                select new
                {
                    candidate.AccountId,
                    GrantId = candidate.Id,
                    candidate.PlanId,
                    PlanKey = plan.Key,
                    PlanIsActive = plan.IsActive,
                    PlanBillingKind = candidate.BillingKindAtGrant,
                    GrantSource = candidate.Source,
                    candidate.GrantedAt,
                    candidate.RevokedAt,
                    candidate.AssignedInvitationId,
                    candidate.ReservedAt,
                    candidate.ConsumedAt,
                    Values = dbContext.PlanEntitlements
                        .AsNoTracking()
                        .Where(entitlement => entitlement.PlanId == candidate.PlanId)
                        .OrderBy(entitlement => entitlement.EntitlementKey)
                        .Select(entitlement => new PlanEntitlementValueSnapshot(
                            entitlement.EntitlementKey,
                            entitlement.NumericValue,
                            entitlement.BooleanValue))
                        .ToList()
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (grant is null)
        {
            return null;
        }

        var paidThroughAtUtc = grant.GrantSource == GrantSource.OrganizationSubscription
            ? await organizationSubscriptions.GetPaidThroughAtUtcAsync(grant.AccountId, grant.PlanId, cancellationToken)
            : null;

        return new EntitlementGrantSnapshot(
            grant.AccountId,
            grant.GrantId,
            grant.PlanId,
            grant.PlanKey,
            grant.PlanIsActive,
            grant.PlanBillingKind,
            grant.GrantSource,
            grant.GrantedAt,
            grant.RevokedAt,
            grant.AssignedInvitationId,
            grant.ReservedAt,
            grant.ConsumedAt,
            grant.Values,
            paidThroughAtUtc);
    }
}
