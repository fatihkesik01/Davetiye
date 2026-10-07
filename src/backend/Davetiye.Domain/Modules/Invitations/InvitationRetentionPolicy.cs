using System.Globalization;

namespace Davetiye.Domain.Modules.Invitations;

public static class InvitationRetentionPolicy
{
    public const string SettingKey = "deletedInvitationRetentionDays";
    public const int MaximumDays = 365;

    public static int? ReadDays(string valueType, string value) =>
        valueType == "Integer" &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
        days is >= 0 and <= MaximumDays ? days : null;
}
