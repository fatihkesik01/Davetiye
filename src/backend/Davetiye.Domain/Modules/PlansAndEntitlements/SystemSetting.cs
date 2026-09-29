namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// A DB-managed, typed global business setting, per docs/PRODUCT.md §21 ("değişmesi muhtemel
/// global iş kuralları mümkün olduğunca DB üzerinden yönetilir", e.g.
/// <c>deletedInvitationRetentionDays</c>). Schema only: this milestone does not seed any concrete
/// setting row or value, since the actual retention/business figures belong to the feature
/// milestone that uses them, not to this foundation schema.
///
/// Placement note: docs/adr/0001's module table nominally assigns general "settings" ownership to
/// the not-yet-coded Administration module. Phase 1 task 8 explicitly authorizes placing
/// SystemSetting under Plans &amp; Entitlements instead (a small shared settings concept was the
/// alternative offered); it is placed here to stay within the two modules this milestone actually
/// touches, given how closely a typed business-limit setting mirrors a typed entitlement. Flagged
/// for Reviewer: this is a placement judgment call, not a product decision.
/// </summary>
public sealed class SystemSetting
{
    private SystemSetting()
    {
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public SystemSettingValueType ValueType { get; private set; }

    /// <summary>Serialized value, validated against <see cref="ValueType"/> on every write.</summary>
    public string Value { get; private set; } = string.Empty;

    public long Revision { get; private set; }

    public static SystemSetting Create(Guid id, string key, SystemSettingValueType valueType, string value)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("System setting id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("System setting key is required.", nameof(key));
        }

        ValidateValue(valueType, value);

        return new SystemSetting
        {
            Id = id,
            Key = key.Trim(),
            ValueType = valueType,
            Value = value
        };
    }

    public void UpdateValue(string value)
    {
        ValidateValue(ValueType, value);
        Value = value;
    }

    public long AsInteger() =>
        ValueType == SystemSettingValueType.Integer
            ? long.Parse(Value)
            : throw new InvalidOperationException($"System setting '{Key}' is not an integer.");

    public bool AsBoolean() =>
        ValueType == SystemSettingValueType.Boolean
            ? bool.Parse(Value)
            : throw new InvalidOperationException($"System setting '{Key}' is not a boolean.");

    private static void ValidateValue(SystemSettingValueType valueType, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        switch (valueType)
        {
            case SystemSettingValueType.Integer:
                if (!long.TryParse(value, out _))
                {
                    throw new ArgumentException("Value must be a valid integer.", nameof(value));
                }

                break;

            case SystemSettingValueType.Boolean:
                if (!bool.TryParse(value, out _))
                {
                    throw new ArgumentException("Value must be 'True' or 'False'.", nameof(value));
                }

                break;

            case SystemSettingValueType.String:
                break;

            default:
                throw new NotSupportedException($"Unsupported system setting value type: {valueType}.");
        }
    }
}
