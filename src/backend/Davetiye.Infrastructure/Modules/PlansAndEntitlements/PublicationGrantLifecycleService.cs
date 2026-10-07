using Davetiye.Application.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class PublicationGrantLifecycleService(
    DavetiyeDbContext dbContext, IEffectiveEntitlementResolver resolver, IClock clock)
    : IPublicationGrantLifecycleService
{
    public async Task<IReadOnlyList<PublicationGrantOption>> ListChoicesAsync(
        Guid accountId, Guid invitationId, IReadOnlyCollection<Guid> startedGrantIds,
        CancellationToken cancellationToken)
    {
        var grants = await dbContext.AccountPlanGrants.AsNoTracking()
            .Where(grant => grant.AccountId == accountId).ToListAsync(cancellationToken);
        var plans = await dbContext.Plans.AsNoTracking().Where(plan => plan.IsActive)
            .ToListAsync(cancellationToken);
        var choices = new List<PublicationGrantOption>();
        foreach (var grant in grants)
        {
            var sharedOrganizationGrant = grant.Source == GrantSource.OrganizationSubscription;
            if ((!sharedOrganizationGrant && (grant.ConsumedAt is not null || startedGrantIds.Contains(grant.Id))) ||
                (grant.AssignedInvitationId is not null && grant.AssignedInvitationId != invitationId))
            {
                continue;
            }

            var resolution = await resolver.ResolveAsync(new EntitlementResolutionContext(
                accountId, grant.Id, invitationId, PublicationEntitlementAction.Schedule), cancellationToken);
            var plan = plans.SingleOrDefault(candidate => candidate.Id == grant.PlanId);
            if (resolution.IsGranted && plan is not null)
            {
                var kind = grant.Source switch
                {
                    GrantSource.Free => "free",
                    GrantSource.OrganizationSubscription => "organizationSubscription",
                    _ => "individualPurchase"
                };
                choices.Add(ToOption(grant.Source == GrantSource.Free ? null : grant.Id,
                    plan.DisplayName, kind, resolution.Entitlements!));
            }
        }

        if (grants.All(grant => grant.Source != GrantSource.Free))
        {
            var freePlan = plans.SingleOrDefault(plan => plan.Key == "free" && plan.BillingKind == PlanBillingKind.Free);
            if (freePlan is not null)
            {
                var values = await dbContext.PlanEntitlements.AsNoTracking()
                    .Where(value => value.PlanId == freePlan.Id)
                    .Select(value => new PlanEntitlementValueSnapshot(value.EntitlementKey, value.NumericValue, value.BooleanValue))
                    .ToListAsync(cancellationToken);
                var provisionalId = Guid.NewGuid();
                var provisional = new EntitlementGrantSnapshot(accountId, provisionalId, freePlan.Id,
                    freePlan.Key, freePlan.IsActive, freePlan.BillingKind, GrantSource.Free,
                    clock.UtcNow.ToUniversalTime(), null, null, null, null, values);
                var previewResolver = new EffectiveEntitlementResolver(new PreviewGrantReader(provisional), clock);
                var resolution = await previewResolver.ResolveAsync(new EntitlementResolutionContext(
                    accountId, provisionalId, invitationId, PublicationEntitlementAction.Schedule), cancellationToken);
                if (resolution.IsGranted)
                {
                    choices.Add(ToOption(null, freePlan.DisplayName, "free", resolution.Entitlements!));
                }
            }
        }

        return choices;
    }

    public async Task<bool> ReleaseAsync(Guid accountId, Guid invitationId, Guid grantId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var grant = await LoadGrantAsync(accountId, grantId, cancellationToken);
        if (grant is null)
        {
            return false;
        }

        if (grant.Source == GrantSource.OrganizationSubscription)
        {
            if (grant.AssignedInvitationId is not null || grant.ReservedAt is not null || grant.ConsumedAt is not null ||
                grant.RevokedAt is not null)
            {
                return false;
            }

            // Releasing a scheduled publication only reduces access and must remain possible after
            // the subscription expires. Ownership and the shared/unassigned grant shape are enough.
            return true;
        }

        if (grant.AssignedInvitationId != invitationId || grant.ConsumedAt is not null)
        {
            return false;
        }

        grant.ReleaseReservation(invitationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ConsumeStartedAsync(Guid accountId, Guid invitationId, Guid grantId,
        DateTimeOffset startsAtUtc, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        if (startsAtUtc.Offset != TimeSpan.Zero || startsAtUtc > clock.UtcNow.ToUniversalTime())
        {
            return false;
        }

        var grant = await LoadGrantAsync(accountId, grantId, cancellationToken);
        if (grant is null)
        {
            return false;
        }

        if (grant.Source == GrantSource.OrganizationSubscription)
        {
            if (grant.AssignedInvitationId is not null || grant.ReservedAt is not null || grant.ConsumedAt is not null ||
                grant.RevokedAt is not null)
            {
                return false;
            }

            var resolution = await resolver.ResolveAsync(new EntitlementResolutionContext(
                accountId, grantId, invitationId, PublicationEntitlementAction.Schedule), cancellationToken);
            return resolution.IsGranted && resolution.Entitlements!.GrantSource == GrantSource.OrganizationSubscription;
        }

        if (grant.AssignedInvitationId != invitationId || grant.RevokedAt is not null ||
            grant.ReservedAt is null || grant.GrantedAt > startsAtUtc ||
            grant.ReservedAt < grant.GrantedAt || grant.ReservedAt > startsAtUtc)
        {
            return false;
        }

        if (grant.ConsumedAt is null)
        {
            grant.ConsumeForInvitation(invitationId, startsAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task<AccountPlanGrant?> LoadGrantAsync(Guid accountId, Guid grantId,
        CancellationToken cancellationToken)
    {
        var grant = await dbContext.AccountPlanGrants.SingleOrDefaultAsync(
            candidate => candidate.AccountId == accountId && candidate.Id == grantId, cancellationToken);
        if (grant is not null)
        {
            await dbContext.Entry(grant).ReloadAsync(cancellationToken);
        }

        return grant;
    }

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Grant lifecycle mutation requires the account publication transaction.");
        }
    }

    private static PublicationGrantOption ToOption(Guid? id, string label, string kind,
        EffectiveEntitlementSnapshot entitlements) => new(id, label, kind,
        entitlements.MaxPublishDays, entitlements.MaxActiveInvitations, entitlements.PremiumTemplatesEnabled);

    private sealed class PreviewGrantReader(EntitlementGrantSnapshot snapshot) : IEntitlementGrantReader
    {
        public Task<EntitlementGrantSnapshot?> FindOwnedGrantAsync(Guid accountId, Guid grantId,
            CancellationToken cancellationToken) => Task.FromResult<EntitlementGrantSnapshot?>(snapshot);
    }
}
