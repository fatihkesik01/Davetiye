using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class PersistenceArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Production_contains_exactly_one_DbContext()
    {
        var dbContextTypes = typeof(DavetiyeDbContext).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal([typeof(DavetiyeDbContext)], dbContextTypes);
    }

    [Fact]
    public void Migrations_are_owned_by_the_Infrastructure_assembly()
    {
        Assert.Equal(
            PersistenceConstants.MigrationsAssemblyName,
            typeof(DavetiyeDbContext).Assembly.GetName().Name);
    }

    [Fact]
    public void Api_startup_never_applies_or_creates_the_database_schema()
    {
        var apiRoot = Path.Combine(RepositoryRoot, "src", "backend", "Davetiye.Api");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.DoesNotContain(".Migrate(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".MigrateAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".EnsureCreated(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".EnsureCreatedAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseMigrator_references_only_Infrastructure()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "tools",
            "Davetiye.DatabaseMigrator",
            "Davetiye.DatabaseMigrator.csproj");
        var projectText = File.ReadAllText(projectPath);

        Assert.Contains("Davetiye.Infrastructure.csproj", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Davetiye.Api.csproj", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Davetiye.Application.csproj", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Davetiye.Domain.csproj", projectText, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
