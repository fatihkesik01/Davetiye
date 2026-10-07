using System.Security.Cryptography;
using System.Text;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

/// <summary>
/// Inserts the PRODUCT §19-20 starter commercial catalog without overwriting operator changes.
/// Supported keys, value types and hard ceilings remain code-owned; prices and plan values become
/// DB-managed after their initial insert. Existing invalid rows fail deployment closed rather than
/// being silently normalized.
/// </summary>
public sealed class PlanCatalogInitializer(DavetiyeDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await InitializeCoreAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        var plans = await dbContext.Plans
            .ToDictionaryAsync(plan => plan.Key, StringComparer.Ordinal, cancellationToken);

        ValidateExistingPlans(plans.Values);

        foreach (var definition in SeedPlans)
        {
            if (plans.ContainsKey(definition.Key))
            {
                continue;
            }

            var plan = Plan.Create(
                DeterministicId($"plan:{definition.Key}"),
                definition.Key,
                definition.DisplayName,
                isActive: true,
                definition.PriceAmount,
                definition.Currency,
                definition.BillingKind);
            dbContext.Plans.Add(plan);
            plans.Add(definition.Key, plan);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var existingEntitlements = await dbContext.PlanEntitlements.ToListAsync(cancellationToken);
        ValidateExistingEntitlements(existingEntitlements);
        var existingPairs = existingEntitlements
            .Select(entitlement => (entitlement.PlanId, entitlement.EntitlementKey))
            .ToHashSet();

        foreach (var planDefinition in SeedPlans)
        {
            var plan = plans[planDefinition.Key];
            foreach (var entitlement in planDefinition.Entitlements)
            {
                if (!existingPairs.Add((plan.Id, entitlement.Key)))
                {
                    continue;
                }

                dbContext.PlanEntitlements.Add(PlanEntitlement.Create(
                    DeterministicId($"entitlement:{planDefinition.Key}:{entitlement.Key}"),
                    plan.Id,
                    entitlement.Key,
                    entitlement.NumericValue,
                    entitlement.BooleanValue));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateExistingPlans(IEnumerable<Plan> plans)
    {
        foreach (var plan in plans)
        {
            _ = Plan.Create(
                plan.Id,
                plan.Key,
                plan.DisplayName,
                plan.IsActive,
                plan.PriceAmount,
                plan.Currency,
                plan.BillingKind);
        }
    }

    private static void ValidateExistingEntitlements(IEnumerable<PlanEntitlement> entitlements)
    {
        foreach (var entitlement in entitlements)
        {
            _ = PlanEntitlement.Create(
                entitlement.Id,
                entitlement.PlanId,
                entitlement.EntitlementKey,
                entitlement.NumericValue,
                entitlement.BooleanValue);
        }
    }

    private static Guid DeterministicId(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"davetiye:{key}"));
        return new Guid(bytes[..16]);
    }

    private sealed record SeedPlan(
        string Key,
        string DisplayName,
        decimal PriceAmount,
        string Currency,
        PlanBillingKind BillingKind,
        IReadOnlyList<SeedEntitlement> Entitlements);

    private sealed record SeedEntitlement(
        string Key,
        long? NumericValue,
        bool? BooleanValue)
    {
        public static SeedEntitlement Numeric(string key, long value) => new(key, value, null);

        public static SeedEntitlement Boolean(string key, bool value) => new(key, null, value);
    }

    // Accepted guest media starting values (docs/PHASE_6_PLAN.md, Fatih 2026-10-05): identical in every plan until Phase 8.
    private const long GuestStartingMaxImages = 100;
    private const long GuestStartingMaxVideos = 10;
    private const long GuestStartingMaxImageSizeMb = 10;
    private const long GuestStartingMaxVideoSizeMb = 100;
    private const long GuestStartingMaxVideoDurationSeconds = 60;

    private static IReadOnlyList<SeedPlan> SeedPlans { get; } =
    [
        CreatePlan("free", "Free", 0m, PlanBillingKind.Free,
            maxPublishDays: 1, maxActiveInvitations: 1, maxImages: 5, maxVideos: 0,
            maxImageSizeMb: 5, maxVideoSizeMb: 0, maxVideoDurationSeconds: 0,
            maxRsvpResponses: 50, memoriesEnabled: false, giftRegistryEnabled: false,
            premiumTemplatesEnabled: false),
        CreatePlan("standard", "Standard", 699m, PlanBillingKind.OneTime,
            maxPublishDays: 30, maxActiveInvitations: 1, maxImages: 30, maxVideos: 1,
            maxImageSizeMb: 10, maxVideoSizeMb: 250, maxVideoDurationSeconds: 180,
            maxRsvpResponses: 300, memoriesEnabled: true, giftRegistryEnabled: true,
            premiumTemplatesEnabled: false),
        CreatePlan("premium", "Premium", 1_199m, PlanBillingKind.OneTime,
            maxPublishDays: 90, maxActiveInvitations: 1, maxImages: 100, maxVideos: 5,
            maxImageSizeMb: 10, maxVideoSizeMb: 500, maxVideoDurationSeconds: 600,
            maxRsvpResponses: 1_000, memoriesEnabled: true, giftRegistryEnabled: true,
            premiumTemplatesEnabled: true),
        CreatePlan("organization", "Organization", 2_499m, PlanBillingKind.Monthly,
            maxPublishDays: 365, maxActiveInvitations: 10, maxImages: 250, maxVideos: 10,
            maxImageSizeMb: 10, maxVideoSizeMb: 1_000, maxVideoDurationSeconds: 900,
            maxRsvpResponses: 5_000, memoriesEnabled: true, giftRegistryEnabled: true,
            premiumTemplatesEnabled: true)
    ];

    private static SeedPlan CreatePlan(
        string key,
        string displayName,
        decimal priceAmount,
        PlanBillingKind billingKind,
        long maxPublishDays,
        long maxActiveInvitations,
        long maxImages,
        long maxVideos,
        long maxImageSizeMb,
        long maxVideoSizeMb,
        long maxVideoDurationSeconds,
        long maxRsvpResponses,
        bool memoriesEnabled,
        bool giftRegistryEnabled,
        bool premiumTemplatesEnabled) =>
        new(
            key,
            displayName,
            priceAmount,
            "TRY",
            billingKind,
            [
                SeedEntitlement.Numeric(EntitlementCatalog.MaxPublishDays, maxPublishDays),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxActiveInvitations, maxActiveInvitations),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxImages, maxImages),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxVideos, maxVideos),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxImageSizeMb, maxImageSizeMb),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxVideoSizeMb, maxVideoSizeMb),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxVideoDurationSeconds, maxVideoDurationSeconds),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxGuestImages, GuestStartingMaxImages),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxGuestVideos, GuestStartingMaxVideos),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxGuestImageSizeMb, GuestStartingMaxImageSizeMb),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxGuestVideoSizeMb, GuestStartingMaxVideoSizeMb),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxGuestVideoDurationSeconds, GuestStartingMaxVideoDurationSeconds),
                SeedEntitlement.Numeric(EntitlementCatalog.MaxRsvpResponses, maxRsvpResponses),
                SeedEntitlement.Boolean(EntitlementCatalog.MemoriesEnabled, memoriesEnabled),
                SeedEntitlement.Boolean(EntitlementCatalog.GiftRegistryEnabled, giftRegistryEnabled),
                SeedEntitlement.Boolean(EntitlementCatalog.PremiumTemplatesEnabled, premiumTemplatesEnabled)
            ]);
}
