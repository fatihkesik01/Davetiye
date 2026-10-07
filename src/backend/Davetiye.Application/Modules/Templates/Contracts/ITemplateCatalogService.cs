namespace Davetiye.Application.Modules.Templates.Contracts;

/// <summary>Read-only, public projection of the active template catalog.</summary>
public interface ITemplateCatalogService
{
    Task<IReadOnlyList<TemplateCatalogItem>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolves metadata for an invitation that already pins this template. Inactive templates
    /// remain available to existing Drafts and Published snapshots, but are never returned by the
    /// public catalog or by new-selection resolution.
    /// </summary>
    Task<TemplateCatalogItem?> ResolvePinnedAsync(string templateKey, CancellationToken cancellationToken);
}

public sealed record TemplateCatalogItem(
    string Key,
    string Name,
    string? Description,
    string Category,
    bool IsPremium,
    int RendererVersion,
    string? PreviewImageUrl,
    IReadOnlyList<string> SupportedModules,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> RecommendedFields);
