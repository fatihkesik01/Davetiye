using Davetiye.Domain.Modules.Templates;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class TemplateCatalogInitializerTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Seed_is_idempotent_and_every_active_catalog_row_has_a_compiled_renderer()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
        var registry = new TemplateRendererRegistry();

        await using (var firstContext = CreateDbContext(connectionString))
        {
            await new TemplateCatalogInitializer(firstContext, registry).InitializeAsync(CancellationToken.None);
            var operatorOwnedTemplate = await firstContext.TemplateDefinitions
                .OrderBy(template => template.Key)
                .FirstAsync();
            operatorOwnedTemplate.UpdateMetadata(
                operatorOwnedTemplate.Name,
                operatorOwnedTemplate.Category,
                operatorOwnedTemplate.IsPremium,
                operatorOwnedTemplate.PreviewImageUrl,
                operatorOwnedTemplate.SupportedModules,
                operatorOwnedTemplate.RequiredFields,
                operatorOwnedTemplate.RecommendedFields,
                description: "Operator-owned description");
            await firstContext.SaveChangesAsync();
        }

        await using (var secondContext = CreateDbContext(connectionString))
        {
            await new TemplateCatalogInitializer(secondContext, registry).InitializeAsync(CancellationToken.None);

            var seeded = await secondContext.TemplateDefinitions
                .AsNoTracking()
                .OrderBy(template => template.Key)
                .ToListAsync();

            Assert.Equal(CodeOwnedTemplateCatalog.Definitions.Count, seeded.Count);
            Assert.All(seeded, template =>
            {
                Assert.True(template.IsActive);
                Assert.NotNull(registry.Resolve(template.Key, template.CurrentRendererVersion));
                Assert.Equal(template.Description is null ? 0 : 1, template.Revision);
                Assert.False(string.IsNullOrWhiteSpace(template.PreviewImageUrl));
                Assert.NotEqual("[]", template.SupportedModules);
                Assert.Contains("\"startsAt\"", template.RequiredFields, StringComparison.Ordinal);
                Assert.DoesNotContain("eventDate", template.RequiredFields, StringComparison.Ordinal);
            });
            Assert.Equal("Operator-owned description", seeded[0].Description);
            Assert.All(seeded.Skip(1), template => Assert.Null(template.Description));
        }
    }

    [Fact]
    public async Task Seed_fails_fast_when_an_active_database_row_has_no_compiled_renderer()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
        var registry = new TemplateRendererRegistry();

        await using var context = CreateDbContext(connectionString);
        context.TemplateDefinitions.Add(TemplateDefinition.Create(
            Guid.NewGuid(), "orphaned-template", "Orphaned", "Genel", true, false, 1,
            "/template-previews/orphaned-template.webp", "[]", "[]", "[]"));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new TemplateCatalogInitializer(context, registry).InitializeAsync(CancellationToken.None));

        Assert.Contains("orphaned-template", exception.Message, StringComparison.Ordinal);
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options);

    private static async Task RunMigratorAsync(string connectionString)
    {
        var migratorAssembly = Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin", "Debug", "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(migratorAssembly), $"Migrator assembly was not built: {migratorAssembly}");

        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(migratorAssembly);
        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";

        using var process = System.Diagnostics.Process.Start(startInfo)
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
}
