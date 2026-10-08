namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>
/// Anonymous marketing projection of one active plan (PRODUCT §30a item 5). Every value is read from the
/// DB-managed plan catalog (§19/§20); no internal identifiers, revisions, grants, hard ceilings or provider
/// data are part of this contract. <see cref="BillingPeriod"/> is one of "free", "one-time" or "monthly".
/// </summary>
public sealed record PublicPlanCatalogItem(
    string Key,
    string DisplayName,
    string? Description,
    decimal PriceAmount,
    string Currency,
    string BillingPeriod,
    long MaxPublishDays,
    long MaxActiveInvitations,
    long MaxImages,
    long MaxVideos,
    long MaxRSVPResponses,
    bool MemoriesEnabled,
    bool GiftRegistryEnabled,
    bool PremiumTemplatesEnabled);

public interface IPublicPlanCatalogReader
{
    /// <summary>Lists active plans ordered by price, then key.</summary>
    Task<IReadOnlyList<PublicPlanCatalogItem>> ListActiveAsync(CancellationToken cancellationToken);
}
