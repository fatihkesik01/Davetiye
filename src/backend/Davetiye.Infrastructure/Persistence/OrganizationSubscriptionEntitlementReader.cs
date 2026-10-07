using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Persistence;

/// <summary>Composition-root adapter that gives Plans only a paid-through instant, never Payments entities.</summary>
public sealed class OrganizationSubscriptionEntitlementReader(DavetiyeDbContext db)
    : IOrganizationSubscriptionEntitlementReader
{
    public Task<DateTimeOffset?> GetPaidThroughAtUtcAsync(
        Guid accountId,
        Guid planId,
        CancellationToken cancellationToken) => db.OrganizationSubscriptions.AsNoTracking()
        .Where(subscription => subscription.AccountId == accountId && subscription.PlanId == planId)
        .OrderByDescending(subscription => subscription.PaidThroughAtUtc)
        .Select(subscription => (DateTimeOffset?)subscription.PaidThroughAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
        DateTimeOffset evaluatedAtUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation instant must be UTC.", nameof(evaluatedAtUtc));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));

        return await db.AccountPlanGrants.AsNoTracking()
            .Where(grant => grant.Source == GrantSource.OrganizationSubscription &&
                db.PublicationWindows.Any(window => window.GrantId == grant.Id && window.IsCurrent &&
                    db.Invitations.IgnoreQueryFilters().Any(invitation => invitation.Id == window.InvitationId &&
                        invitation.DeletedAt == null &&
                        (invitation.State == InvitationStoredState.Scheduled ||
                         invitation.State == InvitationStoredState.Active ||
                         invitation.State == InvitationStoredState.Paused))) &&
                !db.OrganizationSubscriptions.Any(subscription => subscription.AccountId == grant.AccountId &&
                    subscription.PlanId == grant.PlanId && subscription.PaidThroughAtUtc > evaluatedAtUtc))
            .OrderBy(grant => grant.Id)
            .Select(grant => grant.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
