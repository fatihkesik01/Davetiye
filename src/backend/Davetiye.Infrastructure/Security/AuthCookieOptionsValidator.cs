using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class AuthCookieOptionsValidator : IValidateOptions<AuthCookieOptions>
{
    public const int MinExpirationHours = 1;
    public const int MaxExpirationHours = 168;
    public const int MaxSecurityStampValidationIntervalSeconds = 900;

    public ValidateOptionsResult Validate(string? name, AuthCookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.ExpirationHours is < MinExpirationHours or > MaxExpirationHours)
        {
            failures.Add(
                $"AuthCookie:ExpirationHours must be between {MinExpirationHours} and {MaxExpirationHours}.");
        }

        if (options.SecurityStampValidationIntervalSeconds is < 0 or > MaxSecurityStampValidationIntervalSeconds)
        {
            failures.Add(
                $"AuthCookie:SecurityStampValidationIntervalSeconds must be between 0 and {MaxSecurityStampValidationIntervalSeconds}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
