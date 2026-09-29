namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// The code-owned catalog of supported entitlement keys (ADR-0004). Keys mirror the example
/// entitlement names already named in docs/PRODUCT.md §19-20 (maxPublishDays, maxImages, etc.) so
/// this milestone does not invent new product-facing names. The hard ceilings below are technical
/// defaults chosen to bound storage/abuse risk (comfortably above every commercial seed figure in
/// docs/PRODUCT.md §19's table) — they are NOT a commercial/product commitment, and Super Admin
/// changing a Plan's commercial value through <see cref="PlanEntitlement"/> can never exceed them.
/// </summary>
public static class EntitlementCatalog
{
    public const string MaxPublishDays = "maxPublishDays";
    public const string MaxActiveInvitations = "maxActiveInvitations";
    public const string MaxImages = "maxImages";
    public const string MaxVideos = "maxVideos";
    public const string MaxImageSizeMb = "maxImageSizeMb";
    public const string MaxVideoSizeMb = "maxVideoSizeMb";
    public const string MaxVideoDurationSeconds = "maxVideoDurationSeconds";
    public const string MaxRsvpResponses = "maxRSVPResponses";
    public const string MemoriesEnabled = "memoriesEnabled";
    public const string GiftRegistryEnabled = "giftRegistryEnabled";
    public const string PremiumTemplatesEnabled = "premiumTemplatesEnabled";

    public static IReadOnlyList<EntitlementDefinition> All { get; } =
    [
        new EntitlementDefinition(MaxPublishDays, EntitlementValueType.Numeric, HardCeiling: 3_650),
        new EntitlementDefinition(MaxActiveInvitations, EntitlementValueType.Numeric, HardCeiling: 10_000),
        new EntitlementDefinition(MaxImages, EntitlementValueType.Numeric, HardCeiling: 10_000),
        new EntitlementDefinition(MaxVideos, EntitlementValueType.Numeric, HardCeiling: 1_000),
        new EntitlementDefinition(MaxImageSizeMb, EntitlementValueType.Numeric, HardCeiling: 1_000),
        new EntitlementDefinition(MaxVideoSizeMb, EntitlementValueType.Numeric, HardCeiling: 10_000),
        new EntitlementDefinition(MaxVideoDurationSeconds, EntitlementValueType.Numeric, HardCeiling: 86_400),
        new EntitlementDefinition(MaxRsvpResponses, EntitlementValueType.Numeric, HardCeiling: 1_000_000),
        new EntitlementDefinition(MemoriesEnabled, EntitlementValueType.Boolean, HardCeiling: 0),
        new EntitlementDefinition(GiftRegistryEnabled, EntitlementValueType.Boolean, HardCeiling: 0),
        new EntitlementDefinition(PremiumTemplatesEnabled, EntitlementValueType.Boolean, HardCeiling: 0)
    ];

    private static readonly IReadOnlyDictionary<string, EntitlementDefinition> ByKey =
        All.ToDictionary(definition => definition.Key, StringComparer.Ordinal);

    public static EntitlementDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition : null;

    public static EntitlementDefinition Require(string key) =>
        Find(key) ?? throw new ArgumentException($"'{key}' is not a supported entitlement key.", nameof(key));
}
