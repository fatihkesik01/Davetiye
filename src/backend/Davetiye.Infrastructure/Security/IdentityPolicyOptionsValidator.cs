using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class IdentityPolicyOptionsValidator : IValidateOptions<IdentityPolicyOptions>
{
    public const int MinPasswordRequiredLength = 8;
    public const int MaxPasswordRequiredLength = 128;
    public const int MinFailedAccessAttempts = 3;
    public const int MaxFailedAccessAttemptsCeiling = 20;
    public const int MinLockoutDurationMinutes = 1;
    public const int MaxLockoutDurationMinutes = 1440;

    public ValidateOptionsResult Validate(string? name, IdentityPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.PasswordRequiredLength is < MinPasswordRequiredLength or > MaxPasswordRequiredLength)
        {
            failures.Add(
                $"IdentityPolicy:PasswordRequiredLength must be between {MinPasswordRequiredLength} and {MaxPasswordRequiredLength}.");
        }

        if (options.MaxFailedAccessAttempts is < MinFailedAccessAttempts or > MaxFailedAccessAttemptsCeiling)
        {
            failures.Add(
                $"IdentityPolicy:MaxFailedAccessAttempts must be between {MinFailedAccessAttempts} and {MaxFailedAccessAttemptsCeiling}.");
        }

        if (options.LockoutDurationMinutes is < MinLockoutDurationMinutes or > MaxLockoutDurationMinutes)
        {
            failures.Add(
                $"IdentityPolicy:LockoutDurationMinutes must be between {MinLockoutDurationMinutes} and {MaxLockoutDurationMinutes}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
