using System.Globalization;

namespace Davetiye.Infrastructure.Modules.Administration;

internal static class SystemSettingRetentionValues
{
    public static int? ReadDays(string valueType, string value) =>
        string.Equals(valueType, "Integer", StringComparison.Ordinal) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
        days is >= 0 and <= 365 ? days : null;
}
