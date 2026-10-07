using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Templates;

/// <summary>
/// Reconciles the starter catalog after schema migration. The API never migrates the schema; the
/// deployment migrator remains responsible for that. A mismatched active DB row is fatal because a
/// selectable template must always have a compiled renderer (ADR-0007).
/// </summary>
public sealed class TemplateCatalogInitializer(
    DavetiyeDbContext dbContext,
    ITemplateRendererRegistry rendererRegistry)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var existingByKey = await dbContext.TemplateDefinitions
            .ToDictionaryAsync(template => template.Key, StringComparer.Ordinal, cancellationToken);

        foreach (var definition in CodeOwnedTemplateCatalog.Definitions)
        {
            if (!existingByKey.TryGetValue(definition.Key, out var template))
            {
                dbContext.TemplateDefinitions.Add(TemplateDefinition.Create(
                    DeterministicId(definition.Key), definition.Key, definition.Name, definition.Category,
                    isActive: true, definition.IsPremium, definition.RendererVersion, definition.PreviewImageUrl,
                    definition.SupportedModules, definition.RequiredFields, definition.RecommendedFields));
                continue;
            }

            // Name/category/active/free-premium/preview/module and field metadata are DB-managed
            // after their first insert (ADR-0007). A deploy only owns the renderer pin: preserving
            // other operator changes is what makes catalog metadata manageable without a deploy.
            if (template.CurrentRendererVersion != definition.RendererVersion)
            {
                template.SetCurrentRendererVersion(definition.RendererVersion);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var activeTemplates = await dbContext.TemplateDefinitions
            .AsNoTracking()
            .Where(template => template.IsActive)
            .Select(template => new { template.Key, template.CurrentRendererVersion })
            .ToListAsync(cancellationToken);

        var unresolved = activeTemplates.FirstOrDefault(template =>
            rendererRegistry.Resolve(template.Key, template.CurrentRendererVersion) is null);
        if (unresolved is not null)
        {
            throw new InvalidOperationException(
                $"Active template '{unresolved.Key}' version {unresolved.CurrentRendererVersion} has no compiled renderer.");
        }
    }

    private static Guid DeterministicId(string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"davetiye:template:{key}"));
        return new Guid(bytes[..16]);
    }
}
