using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class PaymentPlanCatalogReader(DavetiyeDbContext db) : IPaymentPlanCatalogReader
{
    public async Task<IReadOnlyList<IndividualPurchasePlan>> ListIndividualPlansAsync(CancellationToken cancellationToken) =>
        await db.Plans.AsNoTracking()
            .Where(plan => plan.IsActive && (plan.Key == "standard" || plan.Key == "premium") &&
                plan.BillingKind == PlanBillingKind.OneTime && plan.PriceAmount > 0m && plan.Currency == "TRY")
            .OrderBy(plan => plan.PriceAmount)
            .Select(plan => new IndividualPurchasePlan(plan.Key, plan.DisplayName, plan.PriceAmount, plan.Currency, "one-time"))
            .ToListAsync(cancellationToken);

    public async Task<PurchasablePlanSnapshot?> FindIndividualPlanAsync(string planKey, CancellationToken cancellationToken) =>
        await db.Plans.AsNoTracking()
            .Where(plan => plan.IsActive && plan.Key == planKey && (plan.Key == "standard" || plan.Key == "premium") &&
                plan.BillingKind == PlanBillingKind.OneTime && plan.PriceAmount > 0m && plan.Currency == "TRY")
            .Select(plan => new PurchasablePlanSnapshot(plan.Id, plan.Key, plan.DisplayName, plan.PriceAmount, plan.Currency,
                MapBillingKind(plan.BillingKind)))
            .SingleOrDefaultAsync(cancellationToken);

    private static PurchasableBillingKind MapBillingKind(PlanBillingKind billingKind) => billingKind switch
    {
        PlanBillingKind.OneTime => PurchasableBillingKind.OneTime,
        PlanBillingKind.Monthly => PurchasableBillingKind.Monthly,
        _ => throw new InvalidOperationException("Free plans are not purchasable through Payments."),
    };

    public Task<bool> HasActivePaidGrantAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
        db.AccountPlanGrants.AsNoTracking().AnyAsync(grant => grant.AccountId == accountId &&
            grant.AssignedInvitationId == invitationId && grant.Source == GrantSource.IndividualPurchase && grant.RevokedAt == null,
            cancellationToken);
}
