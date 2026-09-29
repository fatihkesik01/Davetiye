using Microsoft.Extensions.Options;

namespace Davetiye.Api.Infrastructure.Hosting;

/// <summary>
/// Registers this API's own configurable, validated hosting options, following the same
/// <see cref="IValidateOptions{TOptions}"/> + <c>ValidateOnStart()</c> convention already
/// established for the database configuration options.
/// </summary>
public static class ApiHostingOptionsExtensions
{
    public static IServiceCollection AddApiHostingOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RequestLimitsOptions>()
            .Bind(configuration.GetSection(RequestLimitsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RequestLimitsOptions>, RequestLimitsOptionsValidator>();

        services
            .AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection(TrustedProxyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();

        return services;
    }
}
