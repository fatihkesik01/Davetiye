using Davetiye.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

/// <summary>
/// Direct unit tests for M6a's 7 new <see cref="IValidateOptions{TOptions}"/> validators, following
/// the same "valid default passes / a genuine boundary value is rejected" pattern already
/// established by <c>ApiHostingOptionsTests</c>. Exercises each validator through
/// <see cref="SecurityServiceCollectionExtensions.AddAuthSecurityOptions"/> - the narrow
/// options-only registration surface split out specifically so these tests do not need to spin up
/// the rest of the security pipeline (Identity, cookie auth, antiforgery, CORS, rate limiting, Data
/// Protection).
/// </summary>
public sealed class AuthSecurityOptionsTests
{
    [Fact]
    public void IdentityPolicy_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<IdentityPolicyOptions>>().Value;

        Assert.Equal(10, options.PasswordRequiredLength);
    }

    [Fact]
    public void IdentityPolicy_rejects_a_password_required_length_below_the_minimum()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["IdentityPolicy:PasswordRequiredLength"] = "7",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<IdentityPolicyOptions>>().Value);
        Assert.Contains("PasswordRequiredLength must be between", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthCookie_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<AuthCookieOptions>>().Value;

        Assert.Equal(12, options.ExpirationHours);
    }

    [Fact]
    public void AuthCookie_rejects_an_expiration_below_the_minimum_hour()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AuthCookie:ExpirationHours"] = "0",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<AuthCookieOptions>>().Value);
        Assert.Contains("ExpirationHours must be between", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailTokens_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<EmailTokenOptions>>().Value;

        Assert.Equal(60, options.TokenLifetimeMinutes);
    }

    [Fact]
    public void EmailTokens_rejects_a_non_positive_lifetime()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EmailTokens:TokenLifetimeMinutes"] = "0",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<EmailTokenOptions>>().Value);
        Assert.Contains("TokenLifetimeMinutes must be between", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cors_default_value_is_valid_outside_production()
    {
        using var provider = BuildProvider([], isProduction: false);

        var options = provider.GetRequiredService<IOptions<DavetiyeCorsOptions>>().Value;

        Assert.Empty(options.AllowedOrigins);
    }

    [Fact]
    public void Cors_rejects_a_wildcard_origin()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "*",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DavetiyeCorsOptions>>().Value);
        Assert.Contains("must not contain a wildcard origin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthRateLimits_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;

        Assert.Equal(10, options.Login.PermitLimit);
    }

    [Fact]
    public void AuthRateLimits_rejects_a_non_positive_permit_limit()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AuthRateLimits:Login:PermitLimit"] = "0",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value);
        Assert.Contains("Login:PermitLimit must be between", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataProtectionKeyRing_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<DataProtectionKeyRingOptions>>().Value;

        Assert.Equal(".dataprotection-keys", options.KeyRingDirectory);
    }

    [Fact]
    public void DataProtectionKeyRing_rejects_an_empty_directory()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingDirectory"] = "",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DataProtectionKeyRingOptions>>().Value);
        Assert.Contains("KeyRingDirectory is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicWeb_default_value_is_valid_outside_production()
    {
        using var provider = BuildProvider([], isProduction: false);

        var options = provider.GetRequiredService<IOptions<PublicWebOptions>>().Value;

        Assert.Equal("http://localhost:5173", options.BaseUrl);
    }

    [Fact]
    public void PublicWeb_rejects_a_non_absolute_url()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["PublicWeb:BaseUrl"] = "not-a-url",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<PublicWebOptions>>().Value);
        Assert.Contains("must be an absolute http(s) URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicWeb_rejects_a_plain_http_base_url_in_production()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?> { ["PublicWeb:BaseUrl"] = "http://davetiye.example.test" },
            isProduction: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<PublicWebOptions>>().Value);
        Assert.Contains("must use HTTPS in Production", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleAuth_default_value_is_valid_even_in_production()
    {
        // Disabled (the default) is always valid, in every environment: the whole point of the
        // fail-closed gate is that a Production deployment with no verified domain+HTTPS yet can
        // still start up cleanly with Google sign-in simply turned off.
        using var provider = BuildProvider([], isProduction: true);

        var options = provider.GetRequiredService<IOptions<GoogleAuthOptions>>().Value;

        Assert.False(options.Enabled);
    }

    [Fact]
    public void GoogleAuth_enabled_in_production_rejects_a_raw_ip_callback_host()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["GoogleAuth:Enabled"] = "true",
                ["GoogleAuth:ClientId"] = "client-id",
                ["GoogleAuth:ClientSecret"] = "client-secret",
                ["GoogleAuth:CallbackBaseUrl"] = "https://203.0.113.10/api/v1/auth/google/oauth-callback",
            },
            isProduction: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GoogleAuthOptions>>().Value);
        Assert.Contains("verified domain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleAuth_enabled_in_production_rejects_a_plain_http_callback()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["GoogleAuth:Enabled"] = "true",
                ["GoogleAuth:ClientId"] = "client-id",
                ["GoogleAuth:ClientSecret"] = "client-secret",
                ["GoogleAuth:CallbackBaseUrl"] = "http://davetiye.example.test/api/v1/auth/google/oauth-callback",
            },
            isProduction: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GoogleAuthOptions>>().Value);
        Assert.Contains("must use HTTPS in Production", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleAuth_enabled_outside_production_accepts_a_localhost_callback()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = "client-id",
            ["GoogleAuth:ClientSecret"] = "client-secret",
        });

        var options = provider.GetRequiredService<IOptions<GoogleAuthOptions>>().Value;

        Assert.True(options.Enabled);
    }

    [Fact]
    public void GoogleAuth_enabled_without_a_client_secret_is_rejected()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = "client-id",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GoogleAuthOptions>>().Value);
        Assert.Contains("ClientSecret is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mfa_default_value_is_valid()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<MfaOptions>>().Value;

        Assert.Equal(10, options.RecoveryCodeCount);
    }

    [Fact]
    public void Mfa_rejects_a_recovery_code_count_below_the_minimum()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Mfa:RecoveryCodeCount"] = "1",
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<MfaOptions>>().Value);
        Assert.Contains("RecoveryCodeCount must be between", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, bool isProduction = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddAuthSecurityOptions(configuration, isProduction);

        return services.BuildServiceProvider();
    }
}
