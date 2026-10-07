namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// A Plan's actual value for one entitlement key (e.g. "Standard plan -> maxPublishDays = 30"),
/// per ADR-0004. The value's type must match the key's <see cref="EntitlementCatalog"/> declared
/// type, and a numeric value must never exceed that key's hard ceiling. Both rules are enforced
/// here, in the entity itself, rather than by a bypassable validator: the constructor is private,
/// so the only way application code can produce an instance is through <see cref="Create"/> (or
/// mutate one through <see cref="UpdateValue"/>), both of which always run
/// <see cref="ValidateValue"/>. This is the schema behind Phase 1 task 8's "hard-ceiling/value-type
/// sınırı testli" acceptance criterion.
/// </summary>
public sealed class PlanEntitlement
{
    private PlanEntitlement()
    {
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public string EntitlementKey { get; private set; } = string.Empty;

    public long? NumericValue { get; private set; }

    public bool? BooleanValue { get; private set; }

    public long Revision { get; private set; }

    public static PlanEntitlement Create(
        Guid id,
        Guid planId,
        string entitlementKey,
        long? numericValue,
        bool? booleanValue)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Plan entitlement id must not be empty.", nameof(id));
        }

        if (planId == Guid.Empty)
        {
            throw new ArgumentException("Plan entitlement must reference a plan.", nameof(planId));
        }

        var definition = EntitlementCatalog.Require(entitlementKey);
        ValidateValue(definition, numericValue, booleanValue);

        return new PlanEntitlement
        {
            Id = id,
            PlanId = planId,
            EntitlementKey = definition.Key,
            NumericValue = numericValue,
            BooleanValue = booleanValue
        };
    }

    public void UpdateValue(long? numericValue, bool? booleanValue)
    {
        var definition = EntitlementCatalog.Require(EntitlementKey);
        ValidateValue(definition, numericValue, booleanValue);

        if (NumericValue == numericValue && BooleanValue == booleanValue)
        {
            return;
        }

        NumericValue = numericValue;
        BooleanValue = booleanValue;
        Revision++;
    }

    private static void ValidateValue(
        EntitlementDefinition definition,
        long? numericValue,
        bool? booleanValue)
    {
        switch (definition.ValueType)
        {
            case EntitlementValueType.Numeric:
                if (booleanValue is not null)
                {
                    throw new ArgumentException(
                        $"Entitlement '{definition.Key}' is numeric and must not set a boolean value.",
                        nameof(booleanValue));
                }

                if (numericValue is null)
                {
                    throw new ArgumentException(
                        $"Entitlement '{definition.Key}' requires a numeric value.",
                        nameof(numericValue));
                }

                if (numericValue < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(numericValue),
                        numericValue,
                        $"Entitlement '{definition.Key}' value must not be negative.");
                }

                if (numericValue > definition.HardCeiling)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(numericValue),
                        numericValue,
                        $"Entitlement '{definition.Key}' value {numericValue} exceeds the hard ceiling of {definition.HardCeiling}.");
                }

                break;

            case EntitlementValueType.Boolean:
                if (numericValue is not null)
                {
                    throw new ArgumentException(
                        $"Entitlement '{definition.Key}' is boolean and must not set a numeric value.",
                        nameof(numericValue));
                }

                if (booleanValue is null)
                {
                    throw new ArgumentException(
                        $"Entitlement '{definition.Key}' requires a boolean value.",
                        nameof(booleanValue));
                }

                break;

            default:
                throw new NotSupportedException($"Unsupported entitlement value type: {definition.ValueType}.");
        }
    }
}
