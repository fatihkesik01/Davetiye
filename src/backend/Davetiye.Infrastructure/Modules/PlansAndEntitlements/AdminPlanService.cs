using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class AdminPlanService(DavetiyeDbContext db, IAdminAuditWriter audit, IClock clock) : IAdminPlanService
{
    public async Task<IReadOnlyList<AdminPlanItem>> ListAsync(CancellationToken cancellationToken)
    {
        var plans = await db.Plans.AsNoTracking().OrderBy(plan => plan.Key)
            .Select(plan => new { plan.Id, plan.Key, plan.DisplayName, plan.Description, plan.PriceAmount, plan.Currency, plan.BillingKind, plan.Revision })
            .ToListAsync(cancellationToken);
        var ids = plans.Select(plan => plan.Id).ToArray();
        var entitlements = await db.PlanEntitlements.AsNoTracking().Where(value => ids.Contains(value.PlanId))
            .OrderBy(value => value.EntitlementKey).ToListAsync(cancellationToken);
        return plans.Select(plan => new AdminPlanItem(plan.Id, plan.Key, plan.DisplayName, plan.Description,
            plan.PriceAmount, plan.Currency, plan.BillingKind,
            entitlements.Where(value => value.PlanId == plan.Id).Select(ToItem).ToArray(), plan.Revision)).ToArray();
    }

    public async Task<AdminPlanUpdateResult> UpdateAsync(Guid actorId, Guid planId, AdminPlanUpdateRequest request, CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || planId == Guid.Empty || request.ExpectedRevision < 0 ||
            string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 200 ||
            request.Description?.Trim().Length > Plan.DescriptionMaxLength ||
            request.PriceAmount < 0 || request.PriceAmount > Plan.MaxPriceAmount || decimal.Round(request.PriceAmount, 4) != request.PriceAmount ||
            (request.BillingKind == PlanBillingKind.Free ? request.PriceAmount != 0m : request.PriceAmount <= 0m) ||
            !Enum.IsDefined(request.BillingKind) ||
            request.Entitlements is null || request.Entitlements.Count != EntitlementCatalog.All.Count ||
            request.Entitlements.Any(value => value is null) ||
            request.Entitlements.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() != EntitlementCatalog.All.Count ||
            !request.Entitlements.Select(value => value.Key).ToHashSet(StringComparer.Ordinal).SetEquals(EntitlementCatalog.All.Select(value => value.Key)))
            return new(AdminPlanUpdateOutcome.InvalidRequest);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var plan = await db.Plans.SingleOrDefaultAsync(value => value.Id == planId, cancellationToken);
        if (plan is null) return new(AdminPlanUpdateOutcome.NotFound);
        if (plan.Revision != request.ExpectedRevision) return new(AdminPlanUpdateOutcome.Conflict);
        try
        {
            var originalRevision = plan.Revision;
            var rows = await db.PlanEntitlements.Where(value => value.PlanId == planId).ToListAsync(cancellationToken);
            var expected = request.Entitlements.ToDictionary(value => value.Key, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var value = expected[row.EntitlementKey];
                row.UpdateValue(value.NumericValue, value.BooleanValue);
            }
            plan.UpdateAdminMetadata(request.DisplayName, request.Description, request.PriceAmount, request.BillingKind);
            if (plan.Revision == originalRevision && rows.Any(row => db.Entry(row).Property(value => value.Revision).IsModified))
                plan.AdvanceRevision();
            if (plan.Revision != originalRevision)
            {
                audit.Add(actorId, clock.UtcNow, "PlanSettingsUpdated", plan.Id);
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(AdminPlanUpdateOutcome.Succeeded, await GetItemAsync(planId, cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminPlanUpdateOutcome.Conflict);
        }
        catch (ArgumentException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminPlanUpdateOutcome.InvalidRequest);
        }
    }

    private async Task<AdminPlanItem> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleAsync(value => value.Id == id, cancellationToken);
        var entitlements = await db.PlanEntitlements.AsNoTracking().Where(value => value.PlanId == id)
            .OrderBy(value => value.EntitlementKey).Select(value => new AdminPlanEntitlementItem(value.EntitlementKey, value.NumericValue, value.BooleanValue)).ToListAsync(cancellationToken);
        return new(plan.Id, plan.Key, plan.DisplayName, plan.Description, plan.PriceAmount, plan.Currency, plan.BillingKind, entitlements, plan.Revision);
    }

    private static AdminPlanEntitlementItem ToItem(PlanEntitlement value) => new(value.EntitlementKey, value.NumericValue, value.BooleanValue);
}
