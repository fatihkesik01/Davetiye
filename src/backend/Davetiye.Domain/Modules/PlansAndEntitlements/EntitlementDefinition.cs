namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// One supported entitlement key: its value type and its hard ceiling. Per ADR-0004 ("Supported
/// key/type/validation ve hard ceiling kod sözleşmesidir"), this is a code contract, not a
/// DB-editable table — Super Admin can change a <see cref="PlanEntitlement"/>'s commercial value,
/// never the set of supported keys, their type, or how high a value may ever go.
/// </summary>
/// <param name="Key">Stable identifier stored on <see cref="PlanEntitlement.EntitlementKey"/>.</param>
/// <param name="ValueType">Whether this entitlement carries a numeric limit or a boolean toggle.</param>
/// <param name="HardCeiling">
/// For <see cref="EntitlementValueType.Numeric"/> keys, the maximum value any <see cref="PlanEntitlement"/>
/// may ever hold, independent of environment or Super Admin input, per docs/PRODUCT.md §21
/// ("backend ayrıca hard ceiling uygular"). Ignored for boolean keys (a boolean has no numeric
/// ceiling; it is bounded by its type alone).
/// </param>
public sealed record EntitlementDefinition(string Key, EntitlementValueType ValueType, long HardCeiling);
