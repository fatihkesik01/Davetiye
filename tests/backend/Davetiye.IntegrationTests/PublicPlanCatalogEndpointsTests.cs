using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed partial class PublicPlanCatalogEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string Path = "/api/v1/public/plans";

    private static readonly string[] AllowedFields =
    [
        "key", "displayName", "description", "priceAmount", "currency", "billingPeriod", "maxPublishDays",
        "maxActiveInvitations", "maxImages", "maxVideos", "maxRSVPResponses", "memoriesEnabled",
        "giftRegistryEnabled", "premiumTemplatesEnabled",
    ];

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_caller_gets_the_seeded_active_catalog_as_a_safe_projection()
    {
        await using var factory = new PlanCatalogApiFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await client.GetAsync(new Uri(Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotMatch(GuidPattern(), body);
        using var document = JsonDocument.Parse(body);
        var plans = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["free", "standard", "premium", "organization"], plans.Select(plan => plan.GetProperty("key").GetString()));
        Assert.All(plans, plan => Assert.Equal(AllowedFields.Order(StringComparer.Ordinal),
            plan.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.Equal(["free", "one-time", "one-time", "monthly"], plans.Select(plan => plan.GetProperty("billingPeriod").GetString()));

        // Every displayed value must equal the DB row, not a code constant.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        foreach (var plan in plans)
        {
            var key = plan.GetProperty("key").GetString();
            var row = await db.Plans.AsNoTracking().SingleAsync(value => value.Key == key);
            Assert.Equal(row.PriceAmount, plan.GetProperty("priceAmount").GetDecimal());
            Assert.Equal(row.Currency, plan.GetProperty("currency").GetString());
            Assert.Equal(row.DisplayName, plan.GetProperty("displayName").GetString());
            var entitlements = await db.PlanEntitlements.AsNoTracking().Where(value => value.PlanId == row.Id)
                .ToDictionaryAsync(value => value.EntitlementKey);
            Assert.Equal(entitlements[EntitlementCatalog.MaxPublishDays].NumericValue, (long?)plan.GetProperty("maxPublishDays").GetInt64());
            Assert.Equal(entitlements[EntitlementCatalog.MaxRsvpResponses].NumericValue, (long?)plan.GetProperty("maxRSVPResponses").GetInt64());
            Assert.Equal(entitlements[EntitlementCatalog.PremiumTemplatesEnabled].BooleanValue,
                (bool?)plan.GetProperty("premiumTemplatesEnabled").GetBoolean());
        }
    }

    [Fact]
    public async Task Admin_price_and_limit_changes_and_deactivation_are_reflected_without_deployment()
    {
        await using var factory = new PlanCatalogApiFactory(connectionString);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminPlanService>();
            var standard = (await admin.ListAsync(CancellationToken.None)).Single(plan => plan.Key == "standard");
            var entitlements = standard.Entitlements.Select(value => value.Key == EntitlementCatalog.MaxImages
                ? value with { NumericValue = 42 }
                : value).ToArray();
            var result = await admin.UpdateAsync(Guid.NewGuid(), standard.Id, new AdminPlanUpdateRequest(standard.Revision,
                standard.DisplayName, "Kartta görünen açıklama", 749.5m, standard.BillingKind, entitlements), CancellationToken.None);
            Assert.Equal(AdminPlanUpdateOutcome.Succeeded, result.Outcome);

            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var premium = await db.Plans.SingleAsync(plan => plan.Key == "premium");
            premium.SetActive(false);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await client.GetAsync(new Uri(Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = document.RootElement.EnumerateArray().ToArray();
        Assert.DoesNotContain(plans, plan => plan.GetProperty("key").GetString() == "premium");
        var updated = plans.Single(plan => plan.GetProperty("key").GetString() == "standard");
        Assert.Equal(749.5m, updated.GetProperty("priceAmount").GetDecimal());
        Assert.Equal(42L, updated.GetProperty("maxImages").GetInt64());
        Assert.Equal("Kartta görünen açıklama", updated.GetProperty("description").GetString());
    }

    [Fact]
    public void Endpoint_is_anonymous_and_uses_the_public_read_rate_limit_policy()
    {
        using var factory = new PlanCatalogApiFactory(connectionString);
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText?.TrimStart('/') == Path.TrimStart('/') &&
                value.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(AuthRateLimitPolicyNames.PublicInvitationRead,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    private static async Task RunMigratorAsync(string connectionString)
    {
        var migratorAssembly = System.IO.Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin",
            TestBuildConfiguration.Name, "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(migratorAssembly), $"Migrator assembly was not built: {migratorAssembly}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(migratorAssembly);
        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the database migrator process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator failed.{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "Davetiye.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class PlanCatalogApiFactory(string databaseConnectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = databaseConnectionString,
                    ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                    ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                });
            });
        }
    }
}
