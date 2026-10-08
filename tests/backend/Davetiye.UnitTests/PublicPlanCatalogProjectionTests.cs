using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PublicPlanCatalogProjectionTests
{
    [Fact]
    public void Projection_orders_by_price_then_key_and_maps_billing_periods()
    {
        var monthly = Plan.Create(Guid.NewGuid(), "organization", "Organization", true, 2499m, "TRY", PlanBillingKind.Monthly);
        var free = Plan.Create(Guid.NewGuid(), "free", "Free", true, 0m, "TRY", PlanBillingKind.Free, " Basic ");
        var oneTimeB = Plan.Create(Guid.NewGuid(), "zeta", "Zeta", true, 699m, "TRY", PlanBillingKind.OneTime);
        var oneTimeA = Plan.Create(Guid.NewGuid(), "alpha", "Alpha", true, 699m, "TRY", PlanBillingKind.OneTime);
        Plan[] plans = [monthly, free, oneTimeB, oneTimeA];

        var items = PublicPlanCatalogReader.Project(plans, plans.SelectMany(plan => Entitlements(plan.Id, 7)));

        Assert.Equal(["free", "alpha", "zeta", "organization"], items.Select(item => item.Key));
        Assert.Equal(["free", "one-time", "one-time", "monthly"], items.Select(item => item.BillingPeriod));
        Assert.Equal("Basic", items[0].Description);
        Assert.All(items, item =>
        {
            Assert.Equal(7, item.MaxPublishDays);
            Assert.Equal(8, item.MaxActiveInvitations);
            Assert.Equal(9, item.MaxImages);
            Assert.Equal(1, item.MaxVideos);
            Assert.Equal(50, item.MaxRSVPResponses);
            Assert.True(item.MemoriesEnabled);
            Assert.False(item.GiftRegistryEnabled);
            Assert.True(item.PremiumTemplatesEnabled);
        });
    }

    [Fact]
    public void Projection_excludes_inactive_plans_and_plans_missing_a_displayed_entitlement()
    {
        var active = Plan.Create(Guid.NewGuid(), "standard", "Standard", true, 699m, "TRY", PlanBillingKind.OneTime);
        var inactive = Plan.Create(Guid.NewGuid(), "legacy", "Legacy", false, 99m, "TRY", PlanBillingKind.OneTime);
        var incomplete = Plan.Create(Guid.NewGuid(), "partial", "Partial", true, 10m, "TRY", PlanBillingKind.OneTime);
        var entitlements = Entitlements(active.Id, 30)
            .Concat(Entitlements(inactive.Id, 30))
            .Concat(Entitlements(incomplete.Id, 30).Where(value => value.EntitlementKey != EntitlementCatalog.GiftRegistryEnabled));

        var items = PublicPlanCatalogReader.Project([active, inactive, incomplete], entitlements);

        Assert.Equal("standard", Assert.Single(items).Key);
    }

    private static IEnumerable<PlanEntitlement> Entitlements(Guid planId, long publishDays)
    {
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxPublishDays, publishDays, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxActiveInvitations, 8, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxImages, 9, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxVideos, 1, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxRsvpResponses, 50, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MaxImageSizeMb, 5, null);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.MemoriesEnabled, null, true);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.GiftRegistryEnabled, null, false);
        yield return PlanEntitlement.Create(Guid.NewGuid(), planId, EntitlementCatalog.PremiumTemplatesEnabled, null, true);
    }
}
