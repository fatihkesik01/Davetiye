namespace Davetiye.ArchitectureTests;

internal static class RepositoryProjectPolicy
{
    private static readonly HashSet<string> GeneratedDirectoryNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".vite",
        "artifacts",
        "bin",
        "build",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "playwright-report",
        "TestResults",
        "test-results"
    };

    public static readonly string[] ApprovedProductionProjects =
    [
        "src/backend/Davetiye.Api/Davetiye.Api.csproj",
        "src/backend/Davetiye.Application/Davetiye.Application.csproj",
        "src/backend/Davetiye.Domain/Davetiye.Domain.csproj",
        "src/backend/Davetiye.Infrastructure/Davetiye.Infrastructure.csproj"
    ];

    public static readonly string[] ApprovedTestProjects =
    [
        "tests/backend/Davetiye.ArchitectureTests/Davetiye.ArchitectureTests.csproj",
        "tests/backend/Davetiye.IntegrationTests/Davetiye.IntegrationTests.csproj",
        "tests/backend/Davetiye.UnitTests/Davetiye.UnitTests.csproj"
    ];

    public static readonly string[] ApprovedOperationalToolProjects =
    [
        "tools/Davetiye.AdminBootstrap/Davetiye.AdminBootstrap.csproj",
        "tools/Davetiye.DatabaseMigrator/Davetiye.DatabaseMigrator.csproj",
        "tools/Davetiye.MediaDeletionRetry/Davetiye.MediaDeletionRetry.csproj"
    ];

    public static string[] ApprovedRepositoryProjects =>
    [
        .. ApprovedProductionProjects,
        .. ApprovedTestProjects,
        .. ApprovedOperationalToolProjects
    ];

    public static IReadOnlyList<string> FindUnexpectedProjects(
        IEnumerable<string> discoveredProjects,
        IEnumerable<string> approvedProjects)
    {
        var approved = approvedProjects
            .Select(NormalizePath)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. discoveredProjects
                .Select(NormalizePath)
                .Where(project => !approved.Contains(project))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];
    }

    public static IReadOnlyList<string> FindMissingProjects(
        IEnumerable<string> discoveredProjects,
        IEnumerable<string> approvedProjects)
    {
        var discovered = discoveredProjects
            .Select(NormalizePath)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. approvedProjects
                .Select(NormalizePath)
                .Where(project => !discovered.Contains(project))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];
    }

    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    public static bool IsGeneratedPath(string path) =>
        NormalizePath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(GeneratedDirectoryNames.Contains);
}
