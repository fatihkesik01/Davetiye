using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// M4 "Contract/error/health integration smoke": spins up the real API host (WebApplicationFactory)
/// against a real PostgreSQL database and proves the /api/v1 routing convention, sanitized
/// ProblemDetails, correlation ID propagation, health liveness/readiness separation and OpenAPI
/// document all actually work end to end.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ApiContractTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Health_live_endpoint_reports_healthy_without_touching_the_database()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_ready_endpoint_reports_healthy_against_a_real_postgresql_database()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Live_and_ready_are_genuinely_different_checks()
    {
        // A connection string that cannot be reached makes /health/ready unhealthy while
        // /health/live (process liveness only) must stay healthy regardless.
        await using var factory = CreateFactory(
            "Development",
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=davetiye;Password=davetiye;Timeout=1");
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        var readyResponse = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
    }

    [Fact]
    public async Task Api_v1_prefix_serves_a_mapped_endpoint()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1", document.RootElement.GetProperty("apiVersion").GetString());
    }

    [Fact]
    public async Task OpenApi_document_endpoint_returns_a_valid_document()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        Assert.True(document.RootElement.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/api/v1/system/info", out _));
    }

    [Fact]
    public async Task Unhandled_exception_returns_sanitized_problem_details_in_production()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(ThrowingDiagnosticsStartupFilter.Path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ThrowingDiagnosticsStartupFilter.LeakSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Davetiye.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.InvalidOperationException", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var correlationIdInBody = document.RootElement.GetProperty("correlationId").GetString();

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var headerValues));
        var correlationIdInHeader = Assert.Single(headerValues);
        Assert.False(string.IsNullOrWhiteSpace(correlationIdInBody));
        Assert.Equal(correlationIdInHeader, correlationIdInBody);
    }

    [Fact]
    public async Task Unhandled_exception_in_development_includes_the_exception_message_but_never_a_stack_trace()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(ThrowingDiagnosticsStartupFilter.Path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(ThrowingDiagnosticsStartupFilter.LeakSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Davetiye.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correlation_id_supplied_by_the_caller_is_echoed_back()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "caller-supplied-correlation-id");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.Equal("caller-supplied-correlation-id", Assert.Single(values));
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_the_caller_does_not_supply_one()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(values)));
    }

    private ApiWebApplicationFactory CreateFactory(string environmentName, string? connectionStringOverride = null) =>
        new(environmentName, connectionStringOverride ?? connectionString);

    private sealed class ApiWebApplicationFactory(string environmentName, string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                var config = new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                };

                // M6a's security foundation (Davetiye.Infrastructure.Security.SecurityServiceCollectionExtensions)
                // validates DavetiyeCorsOptions/PublicWebOptions with ValidateOnStart(), which is eager at host
                // startup regardless of whether anything else in this test ever exercises auth. A real Production
                // deployment always configures these; simulating "Production" here without them would fail for a
                // reason unrelated to what this test suite actually verifies (sanitized ProblemDetails).
                if (environmentName == "Production")
                {
                    config["Cors:AllowedOrigins:0"] = "https://davetiye.example.test";
                    config["PublicWeb:BaseUrl"] = "https://davetiye.example.test";
                }

                configurationBuilder.AddInMemoryCollection(config);
            });
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, ThrowingDiagnosticsStartupFilter>());
        }
    }

    /// <summary>
    /// Test-only diagnostic route that deliberately throws, so the error-sanitization contract can
    /// be proven over real HTTP. Added exclusively from the test project via IStartupFilter; it is
    /// never part of the production Davetiye.Api assembly or route table, matching the same
    /// test-only-fixture convention used for M8's BOLA harness.
    /// </summary>
    private sealed class ThrowingDiagnosticsStartupFilter : IStartupFilter
    {
        public const string Path = "/__test-diagnostics/throw";
        public const string LeakSentinel = "sentinel-secret-detail-that-must-never-reach-a-client";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);

            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == Path)
                {
                    throw new InvalidOperationException(LeakSentinel);
                }

                await nextMiddleware(context);
            });
        };
    }
}
