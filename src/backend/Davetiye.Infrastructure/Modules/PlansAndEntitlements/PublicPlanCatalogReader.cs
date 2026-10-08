using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

/// <summary>
/// Read-only, anonymous projection of the active plan catalog for the public landing page. Values come
/// straight from the DB-managed Plans/PlanEntitlements rows so a Super Admin change is visible without a
/// deployment. A plan missing one of the displayed entitlements is not advertised (fail closed) rather
/// than shown with an invented value.
/// </summary>
public sealed class PublicPlanCatalogReader(DavetiyeDbContext db) : IPublicPlanCatalogReader
{
    private static readonly string[] DisplayedEntitlementKeys =
    [
        EntitlementCatalog.MaxPublishDays,
        EntitlementCatalog.MaxActiveInvitations,
        EntitlementCatalog.MaxImages,
        EntitlementCatalog.MaxVideos,
        EntitlementCatalog.MaxRsvpResponses,
        EntitlementCatalog.MemoriesEnabled,
        EntitlementCatalog.GiftRegistryEnabled,
        EntitlementCatalog.PremiumTemplatesEnabled,
    ];

    public async Task<IReadOnlyList<PublicPlanCatalogItem>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var plans = await db.Plans.AsNoTracking().Where(plan => plan.IsActive).ToListAsync(cancellationToken);
        if (plans.Count == 0) return [];
        var planIds = plans.Select(plan => plan.Id).ToArray();
        var entitlements = await db.PlanEntitlements.AsNoTracking()
            .Where(entitlement => planIds.Contains(entitlement.PlanId) &&
                DisplayedEntitlementKeys.Contains(entitlement.EntitlementKey))
            .ToListAsync(cancellationToken);
        return Project(plans, entitlements);
    }

    internal static IReadOnlyList<PublicPlanCatalogItem> Project(
        IEnumerable<Plan> plans, IEnumerable<PlanEntitlement> entitlements)
    {
        var byPlan = entitlements.ToLookup(entitlement => entitlement.PlanId);
        var items = new List<PublicPlanCatalogItem>();
        // Plan has no admin-managed sort order column; price then key gives a deterministic card order.
        foreach (var plan in plans.Where(plan => plan.IsActive)
                     .OrderBy(plan => plan.PriceAmount).ThenBy(plan => plan.Key, StringComparer.Ordinal))
        {
            var values = byPlan[plan.Id].ToDictionary(entitlement => entitlement.EntitlementKey, StringComparer.Ordinal);
            if (!TryNumeric(values, EntitlementCatalog.MaxPublishDays, out var maxPublishDays) ||
                !TryNumeric(values, EntitlementCatalog.MaxActiveInvitations, out var maxActiveInvitations) ||
                !TryNumeric(values, EntitlementCatalog.MaxImages, out var maxImages) ||
                !TryNumeric(values, EntitlementCatalog.MaxVideos, out var maxVideos) ||
                !TryNumeric(values, EntitlementCatalog.MaxRsvpResponses, out var maxRsvpResponses) ||
                !TryBoolean(values, EntitlementCatalog.MemoriesEnabled, out var memoriesEnabled) ||
                !TryBoolean(values, EntitlementCatalog.GiftRegistryEnabled, out var giftRegistryEnabled) ||
                !TryBoolean(values, EntitlementCatalog.PremiumTemplatesEnabled, out var premiumTemplatesEnabled))
            {
                continue;
            }

            items.Add(new PublicPlanCatalogItem(plan.Key, plan.DisplayName, plan.Description, plan.PriceAmount,
                plan.Currency, ToBillingPeriod(plan.BillingKind), maxPublishDays, maxActiveInvitations, maxImages,
                maxVideos, maxRsvpResponses, memoriesEnabled, giftRegistryEnabled, premiumTemplatesEnabled));
        }

        return items;
    }

    // Same wire vocabulary as the existing checkout ("one-time") and Organization subscription ("monthly") contracts.
    internal static string ToBillingPeriod(PlanBillingKind billingKind) => billingKind switch
    {
        PlanBillingKind.Free => "free",
        PlanBillingKind.OneTime => "one-time",
        PlanBillingKind.Monthly => "monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(billingKind), billingKind, "Unsupported billing kind."),
    };

    private static bool TryNumeric(Dictionary<string, PlanEntitlement> values, string key, out long value)
    {
        value = 0;
        if (!values.TryGetValue(key, out var entitlement) || entitlement.NumericValue is not { } numeric) return false;
        value = numeric;
        return true;
    }

    private static bool TryBoolean(Dictionary<string, PlanEntitlement> values, string key, out bool value)
    {
        value = false;
        if (!values.TryGetValue(key, out var entitlement) || entitlement.BooleanValue is not { } boolean) return false;
        value = boolean;
        return true;
    }
}
