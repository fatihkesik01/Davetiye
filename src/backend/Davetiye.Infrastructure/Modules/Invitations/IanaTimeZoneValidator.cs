using Davetiye.Application.Modules.Invitations.Contracts;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class IanaTimeZoneValidator : IIanaTimeZoneValidator
{
    public bool IsValid(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        var normalized = timeZoneId.Trim();
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out _))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
