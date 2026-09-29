using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class DataProtectionKeyRingOptionsValidator : IValidateOptions<DataProtectionKeyRingOptions>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionKeyRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.KeyRingDirectory)
            ? ValidateOptionsResult.Fail("DataProtection:KeyRingDirectory is required.")
            : ValidateOptionsResult.Success;
    }
}
