using System.Net;
using Microsoft.Extensions.Options;

namespace Davetiye.Api.Infrastructure.Hosting;

internal sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    public const int MaxForwardLimit = 10;

    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        foreach (var network in options.Networks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                failures.Add($"TrustedProxies:Networks entry '{network}' is not a valid CIDR network.");
            }
        }

        if (options.ForwardLimit is < 1 or > MaxForwardLimit)
        {
            failures.Add($"TrustedProxies:ForwardLimit must be between 1 and {MaxForwardLimit}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
