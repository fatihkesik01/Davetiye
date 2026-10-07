using System.Diagnostics;
using System.Text.Json;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class TemplateCatalogEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Public_catalog_returns_only_safe_active_template_metadata()
    {
        await using var factory = new TemplateCatalogApiFactory(connectionString);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var inactiveTemplate = await dbContext.TemplateDefinitions
                .SingleAsync(template => template.Key == "romantik-nisan");
            inactiveTemplate.SetActive(false);
            await dbContext.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/templates", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var templates = document.RootElement;
        Assert.Equal(7, templates.GetArrayLength());
        Assert.DoesNotContain(templates.EnumerateArray(), template =>
            template.GetProperty("key").GetString() == "romantik-nisan");

        var wedding = templates.EnumerateArray().Single(template =>
            template.GetProperty("key").GetString() == "zamansiz-dugun");
        Assert.Equal("Zamansız Düğün", wedding.GetProperty("name").GetString());
        Assert.False(wedding.GetProperty("isPremium").GetBoolean());
        // Phase 3 pins newly selected templates to V2 while V1 remains available
        // for already-published invitations.
        Assert.Equal(2, wedding.GetProperty("rendererVersion").GetInt32());
        Assert.NotEmpty(wedding.GetProperty("supportedModules").EnumerateArray());
        Assert.False(wedding.TryGetProperty("isActive", out _));
    }

    private static async Task RunMigratorAsync(string connectionString)
    {
        var migratorAssembly = Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin", "Debug", "net10.0", "Davetiye.DatabaseMigrator.dll");
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
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TemplateCatalogApiFactory(string databaseConnectionString) : WebApplicationFactory<Program>
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
