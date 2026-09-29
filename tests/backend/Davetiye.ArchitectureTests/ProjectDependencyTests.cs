using System.Xml.Linq;
using Xunit;

namespace Davetiye.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        {
            "src/backend/Davetiye.Domain/Davetiye.Domain.csproj",
            []
        },
        {
            "src/backend/Davetiye.Application/Davetiye.Application.csproj",
            ["src/backend/Davetiye.Domain/Davetiye.Domain.csproj"]
        },
        {
            "src/backend/Davetiye.Infrastructure/Davetiye.Infrastructure.csproj",
            [
                "src/backend/Davetiye.Application/Davetiye.Application.csproj",
                "src/backend/Davetiye.Domain/Davetiye.Domain.csproj"
            ]
        },
        {
            "src/backend/Davetiye.Api/Davetiye.Api.csproj",
            [
                "src/backend/Davetiye.Application/Davetiye.Application.csproj",
                "src/backend/Davetiye.Infrastructure/Davetiye.Infrastructure.csproj"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Production_project_references_follow_the_approved_direction(
        string projectPath,
        string[] expectedReferences)
    {
        var fullProjectPath = Path.Combine(RepositoryRoot, ToPlatformPath(projectPath));
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var document = XDocument.Load(fullProjectPath);

        var actualReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, ToPlatformPath(value!))))
            .Select(value => NormalizePath(Path.GetRelativePath(RepositoryRoot, value)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
    }

    [Fact]
    public void Backend_production_project_set_is_exactly_the_approved_M2A_set()
    {
        var discoveredProjects = EnumerateProjectPaths("src/backend");

        Assert.Empty(RepositoryProjectPolicy.FindUnexpectedProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedProductionProjects));
        Assert.Empty(RepositoryProjectPolicy.FindMissingProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedProductionProjects));
    }

    [Fact]
    public void Backend_test_project_set_is_exactly_the_approved_set()
    {
        var discoveredProjects = EnumerateProjectPaths("tests/backend");

        Assert.Empty(RepositoryProjectPolicy.FindUnexpectedProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedTestProjects));
        Assert.Empty(RepositoryProjectPolicy.FindMissingProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedTestProjects));
    }

    [Fact]
    public void Operational_tool_projects_require_an_explicit_milestone_allowlist_entry()
    {
        var discoveredProjects = EnumerateProjectPaths("tools");

        Assert.Empty(RepositoryProjectPolicy.FindUnexpectedProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedOperationalToolProjects));
        Assert.Empty(RepositoryProjectPolicy.FindMissingProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedOperationalToolProjects));
    }

    [Fact]
    public void Repository_wide_project_set_is_exactly_the_approved_union()
    {
        var discoveredProjects = EnumerateRepositoryProjectPaths();

        Assert.Empty(RepositoryProjectPolicy.FindUnexpectedProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedRepositoryProjects));
        Assert.Empty(RepositoryProjectPolicy.FindMissingProjects(
            discoveredProjects,
            RepositoryProjectPolicy.ApprovedRepositoryProjects));
    }

    [Fact]
    public void Solution_contains_exactly_all_approved_production_test_and_tool_projects()
    {
        var solution = XDocument.Load(Path.Combine(RepositoryRoot, "Davetiye.slnx"));
        var solutionProjects = solution
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => RepositoryProjectPolicy.NormalizePath(path!))
            .ToArray();
        var approvedProjects = RepositoryProjectPolicy.ApprovedRepositoryProjects;

        Assert.Empty(RepositoryProjectPolicy.FindUnexpectedProjects(
            solutionProjects,
            approvedProjects));
        Assert.Empty(RepositoryProjectPolicy.FindMissingProjects(
            solutionProjects,
            approvedProjects));
    }

    [Fact]
    public void Closed_project_set_policy_flags_an_unknown_project_without_creating_it()
    {
        string[] approvedProjects = ["src/backend/Davetiye.Api/Davetiye.Api.csproj"];
        string[] discoveredProjects =
        [
            "src/backend/Davetiye.Api/Davetiye.Api.csproj",
            "services/Rogue/Rogue.csproj"
        ];

        var violations = RepositoryProjectPolicy.FindUnexpectedProjects(
            discoveredProjects,
            approvedProjects);

        Assert.Equal(["services/Rogue/Rogue.csproj"], violations);
    }

    [Fact]
    public void Repository_project_policy_ignores_generated_directory_projects()
    {
        Assert.True(RepositoryProjectPolicy.IsGeneratedPath(
            "services/Rogue/obj/Generated.csproj"));
        Assert.False(RepositoryProjectPolicy.IsGeneratedPath(
            "services/Rogue/Rogue.csproj"));
    }

    [Theory]
    [InlineData("src/backend/Davetiye.Domain/Davetiye.Domain.csproj")]
    [InlineData("src/backend/Davetiye.Application/Davetiye.Application.csproj")]
    public void Domain_and_application_have_no_framework_or_provider_package_dependencies(
        string projectPath)
    {
        var fullProjectPath = Path.Combine(RepositoryRoot, ToPlatformPath(projectPath));
        var document = XDocument.Load(fullProjectPath);

        var dependencyNames = document
            .Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Empty(dependencyNames);
    }

    [Fact]
    public void Every_production_layer_has_a_modules_root()
    {
        var moduleRoots = new[]
        {
            "src/backend/Davetiye.Api/Modules",
            "src/backend/Davetiye.Application/Modules",
            "src/backend/Davetiye.Domain/Modules",
            "src/backend/Davetiye.Infrastructure/Modules"
        };

        Assert.All(moduleRoots, relativePath =>
            Assert.True(
                Directory.Exists(Path.Combine(RepositoryRoot, ToPlatformPath(relativePath))),
                $"Missing module root: {relativePath}"));
    }

    [Theory]
    [InlineData("src/backend/Davetiye.Domain")]
    [InlineData("src/backend/Davetiye.Application")]
    public void Domain_and_application_source_do_not_name_concrete_providers(string sourceRoot)
    {
        var fullSourceRoot = Path.Combine(RepositoryRoot, ToPlatformPath(sourceRoot));
        var sourceFiles = Directory.EnumerateFiles(fullSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var violations = sourceFiles
            .Where(path => !ArchitecturePolicy.IsProviderNeutral(File.ReadAllText(path)))
            .Select(path => NormalizePath(Path.GetRelativePath(RepositoryRoot, path)))
            .ToArray();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Application")]
    [InlineData("Domain")]
    [InlineData("Infrastructure")]
    public void Module_source_namespaces_match_their_folder_ownership(string layerName)
    {
        var layerRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "backend",
            $"Davetiye.{layerName}");
        var modulesRoot = Path.Combine(layerRoot, "Modules");
        var violations = Directory
            .EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Violation = ArchitecturePolicy.ValidateModuleSource(
                    layerName,
                    Path.GetRelativePath(layerRoot, path),
                    File.ReadAllText(path))
            })
            .Where(item => item.Violation is not null)
            .Select(item => item.Violation!)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Api_uses_infrastructure_only_from_the_composition_root()
    {
        var apiRoot = Path.Combine(RepositoryRoot, "src", "backend", "Davetiye.Api");
        var violations = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("Program.cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Davetiye.Infrastructure", StringComparison.Ordinal))
            .Select(path => NormalizePath(Path.GetRelativePath(RepositoryRoot, path)))
            .ToArray();

        Assert.Empty(violations);
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

    private static string[] EnumerateProjectPaths(string relativeRoot)
    {
        var fullRoot = Path.Combine(RepositoryRoot, ToPlatformPath(relativeRoot));
        if (!Directory.Exists(fullRoot))
        {
            return [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(fullRoot, "*.csproj", SearchOption.AllDirectories)
                .Select(path => RepositoryProjectPolicy.NormalizePath(
                    Path.GetRelativePath(RepositoryRoot, path)))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static string[] EnumerateRepositoryProjectPaths()
    {
        var enumerationOptions = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        return
        [
            .. Directory
                .EnumerateFiles(RepositoryRoot, "*.csproj", enumerationOptions)
                .Select(path => RepositoryProjectPolicy.NormalizePath(
                    Path.GetRelativePath(RepositoryRoot, path)))
                .Where(path => !RepositoryProjectPolicy.IsGeneratedPath(path))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');
}
