using Davetiye.Api.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class ApiHostingOptionsTests
{
    [Fact]
    public void RequestLimits_default_value_is_valid()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<RequestLimitsOptions>>().Value;

        Assert.Equal(RequestLimitsOptions.DefaultMaxRequestBodyBytes, options.MaxRequestBodyBytes);
    }

    [Fact]
    public void RequestLimits_rejects_a_non_positive_value()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["RequestLimits:MaxRequestBodyBytes"] = "0",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<RequestLimitsOptions>>().Value);
        Assert.Contains("greater than zero", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLimits_enforces_a_hard_ceiling_regardless_of_configuration()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["RequestLimits:MaxRequestBodyBytes"] = (20 * 1024 * 1024 + 1).ToString(),
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<RequestLimitsOptions>>().Value);
        Assert.Contains("hard ceiling", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedProxies_default_value_is_valid_and_trusts_nothing()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;

        Assert.Empty(options.Networks);
    }

    [Theory]
    [InlineData("127.0.0.1/32")]
    [InlineData("::1/128")]
    [InlineData("10.0.0.0/8")]
    public void TrustedProxies_accepts_valid_cidr_networks(string network)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["TrustedProxies:Networks:0"] = network,
        });

        var options = provider.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;

        Assert.Equal([network], options.Networks);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("127.0.0.1")]
    [InlineData("999.999.999.999/32")]
    public void TrustedProxies_rejects_malformed_or_non_cidr_entries(string network)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["TrustedProxies:Networks:0"] = network,
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
        Assert.Contains("not a valid CIDR network", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    public void TrustedProxies_rejects_a_forward_limit_outside_the_allowed_range(string forwardLimit)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["TrustedProxies:ForwardLimit"] = forwardLimit,
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
        Assert.Contains("ForwardLimit must be between", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddApiHostingOptions(configuration);

        return services.BuildServiceProvider();
    }
}
