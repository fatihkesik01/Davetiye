namespace Davetiye.Domain.Modules.Administration;

/// <summary>A DB-managed, typed global business setting owned by Administration.</summary>
public sealed class SystemSetting
{
    private SystemSetting() { }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public SystemSettingValueType ValueType { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public long Revision { get; private set; }

    public static SystemSetting Create(Guid id, string key, SystemSettingValueType valueType, string value)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("System setting id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("System setting key is required.", nameof(key));

        ValidateValue(valueType, value);
        return new SystemSetting { Id = id, Key = key.Trim(), ValueType = valueType, Value = value };
    }

    public void UpdateValue(string value)
    {
        ValidateValue(ValueType, value);
        if (Value == value)
            return;
        Value = value;
        Revision++;
    }

    public long AsInteger() => ValueType == SystemSettingValueType.Integer
        ? long.Parse(Value)
        : throw new InvalidOperationException($"System setting '{Key}' is not an integer.");

    public bool AsBoolean() => ValueType == SystemSettingValueType.Boolean
        ? bool.Parse(Value)
        : throw new InvalidOperationException($"System setting '{Key}' is not a boolean.");

    private static void ValidateValue(SystemSettingValueType valueType, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (valueType)
        {
            case SystemSettingValueType.Integer when !long.TryParse(value, out _):
                throw new ArgumentException("Value must be a valid integer.", nameof(value));
            case SystemSettingValueType.Boolean when !bool.TryParse(value, out _):
                throw new ArgumentException("Value must be 'True' or 'False'.", nameof(value));
            case SystemSettingValueType.Integer or SystemSettingValueType.Boolean or SystemSettingValueType.String:
                break;
            default:
                throw new NotSupportedException($"Unsupported system setting value type: {valueType}.");
        }
    }
}
