using System.Globalization;

namespace Davetiye.Domain.Modules.Memories;

public static class AbandonedMemoryRetentionPolicy
{
    public const string SettingKey = "abandonedMemoryRetentionDays";
    public const int DefaultDays = 30;
    public const int MaximumDays = 365;

    public static int? ReadDays(string valueType, string value) =>
        valueType == "Integer" &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
        days is >= 0 and <= MaximumDays ? days : null;
}
