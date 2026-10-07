using System.Text.Json;
using Davetiye.Infrastructure.Modules.Templates;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class TemplateRendererRegistryTests
{
    [Fact]
    public void Every_code_owned_catalog_entry_resolves_by_its_pinned_key_and_version()
    {
        var registry = new TemplateRendererRegistry();

        Assert.Equal(CodeOwnedTemplateRendererVersions.Registrations.Count, registry.Registrations.Count);
        foreach (var definition in CodeOwnedTemplateCatalog.Definitions)
        {
            var resolved = registry.Resolve(definition.Key, definition.RendererVersion);

            Assert.NotNull(resolved);
            Assert.Equal(definition.Key, resolved.TemplateKey);
            Assert.Equal(definition.RendererVersion, resolved.RendererVersion);
        }
    }

    [Fact]
    public void Registry_does_not_resolve_an_unknown_key_or_version()
    {
        var registry = new TemplateRendererRegistry();
        var known = CodeOwnedTemplateCatalog.Definitions[0];

        Assert.Null(registry.Resolve("not-a-template", 1));
        Assert.Null(registry.Resolve(known.Key, known.RendererVersion + 1));
    }

    [Fact]
    public void Backend_supported_versions_match_the_compiled_React_renderer_manifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Contracts", "registry.manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var frontendRegistrations = manifest.RootElement.GetProperty("supportedRenderers")
            .EnumerateArray()
            .Select(entry => (
                entry.GetProperty("templateKey").GetString()!,
                entry.GetProperty("rendererVersion").GetInt32()))
            .OrderBy(entry => entry.Item1, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item2)
            .ToArray();
        var backendRegistrations = CodeOwnedTemplateRendererVersions.Registrations
            .Select(entry => (entry.TemplateKey, entry.RendererVersion))
            .OrderBy(entry => entry.TemplateKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.RendererVersion)
            .ToArray();

        Assert.Equal(backendRegistrations, frontendRegistrations);
    }

    [Fact]
    public void Every_catalog_preview_points_to_a_non_empty_web_asset()
    {
        foreach (var definition in CodeOwnedTemplateCatalog.Definitions)
        {
            Assert.NotNull(definition.PreviewImageUrl);
            var relativePath = definition.PreviewImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var assetPath = Path.Combine(AppContext.BaseDirectory, "WebPublic", relativePath);

            Assert.True(File.Exists(assetPath), $"Missing preview asset: {definition.PreviewImageUrl}");
            Assert.True(new FileInfo(assetPath).Length > 0, $"Empty preview asset: {definition.PreviewImageUrl}");
        }
    }
}
