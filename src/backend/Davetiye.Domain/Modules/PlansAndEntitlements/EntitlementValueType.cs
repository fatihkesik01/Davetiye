namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// The two entitlement value shapes described in docs/PRODUCT.md §20 (numeric limits like
/// maxImages, boolean module toggles like memoriesEnabled).
/// </summary>
public enum EntitlementValueType
{
    Numeric,
    Boolean
}
