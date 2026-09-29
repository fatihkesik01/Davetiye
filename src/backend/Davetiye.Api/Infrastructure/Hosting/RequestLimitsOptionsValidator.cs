using Microsoft.Extensions.Options;

namespace Davetiye.Api.Infrastructure.Hosting;

/// <summary>
/// Validates <see cref="RequestLimitsOptions"/> at startup, following the same
/// <see cref="IValidateOptions{TOptions}"/> convention already used for database configuration.
/// A configurable business value is still bounded by a security hard ceiling, independent of
/// environment, per docs/THREAT_MODEL.md §21.
/// </summary>
internal sealed class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>
{
    public const long HardCeilingBytes = 20 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, RequestLimitsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.MaxRequestBodyBytes <= 0)
        {
            failures.Add("RequestLimits:MaxRequestBodyBytes must be greater than zero.");
        }
        else if (options.MaxRequestBodyBytes > HardCeilingBytes)
        {
            failures.Add(
                $"RequestLimits:MaxRequestBodyBytes must not exceed the hard ceiling of {HardCeilingBytes} bytes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
