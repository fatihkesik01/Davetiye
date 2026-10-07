using System.Text.Json;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Templates;

public sealed class TemplateCatalogService(DavetiyeDbContext dbContext) : ITemplateCatalogService
{
    public async Task<IReadOnlyList<TemplateCatalogItem>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var templates = await dbContext.TemplateDefinitions
            .AsNoTracking()
            .Where(template => template.IsActive)
            .OrderBy(template => template.Category)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

        return templates.Select(template => new TemplateCatalogItem(
            template.Key, template.Name, template.Description, template.Category, template.IsPremium,
            template.CurrentRendererVersion, template.PreviewImageUrl,
            ReadStringArray(template.SupportedModules), ReadStringArray(template.RequiredFields),
            ReadStringArray(template.RecommendedFields))).ToArray();
    }

    public async Task<TemplateCatalogItem?> ResolvePinnedAsync(
        string templateKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            return null;
        }

        var template = await dbContext.TemplateDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Key == templateKey, cancellationToken);

        return template is null
            ? null
            : new TemplateCatalogItem(
                template.Key, template.Name, template.Description, template.Category, template.IsPremium,
                template.CurrentRendererVersion, template.PreviewImageUrl,
                ReadStringArray(template.SupportedModules), ReadStringArray(template.RequiredFields),
                ReadStringArray(template.RecommendedFields));
    }

    private static IReadOnlyList<string> ReadStringArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString()!)
            .ToArray();
    }
}
