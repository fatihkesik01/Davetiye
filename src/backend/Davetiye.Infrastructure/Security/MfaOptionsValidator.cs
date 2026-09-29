using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class MfaOptionsValidator : IValidateOptions<MfaOptions>
{
    public const int MinRecoveryCodeCount = 4;
    public const int MaxRecoveryCodeCount = 20;

    public ValidateOptionsResult Validate(string? name, MfaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RecoveryCodeCount is < MinRecoveryCodeCount or > MaxRecoveryCodeCount)
        {
            return ValidateOptionsResult.Fail(
                $"Mfa:RecoveryCodeCount must be between {MinRecoveryCodeCount} and {MaxRecoveryCodeCount}.");
        }

        return ValidateOptionsResult.Success;
    }
}
