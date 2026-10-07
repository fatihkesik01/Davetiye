using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class PublicationGrantAccessValidator(
    DavetiyeDbContext dbContext,
    IOrganizationSubscriptionEntitlementReader organizationSubscriptions) : IPublicationGrantAccessValidator
{
    public async Task<PublicationGrantAccessSnapshot?> ReadAsync(Guid accountId, Guid grantId, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.AccountPlanGrants.AsNoTracking()
            .Where(grant => grant.Id == grantId && grant.AccountId == accountId)
            .Select(grant => new PublicationGrantAccessSnapshot(grant.Id, grant.AccountId,
                grant.Source == GrantSource.Free || grant.Source == GrantSource.IndividualPurchase ||
                    grant.Source == GrantSource.OrganizationSubscription,
                grant.AssignedInvitationId, grant.GrantedAt, grant.ReservedAt, grant.ConsumedAt, grant.RevokedAt,
                grant.Source, null, grant.PlanId))
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null || snapshot.GrantSource != GrantSource.OrganizationSubscription)
            return snapshot;

        var paidThroughAtUtc = await organizationSubscriptions.GetPaidThroughAtUtcAsync(
            snapshot.AccountId, snapshot.PlanId, cancellationToken);
        return snapshot with { OrganizationSubscriptionPaidThroughAtUtc = paidThroughAtUtc };
    }

    public Task<IReadOnlyCollection<Guid>> FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
        DateTimeOffset evaluatedAtUtc, int limit, CancellationToken cancellationToken) =>
        organizationSubscriptions.FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
            evaluatedAtUtc, limit, cancellationToken);
}
