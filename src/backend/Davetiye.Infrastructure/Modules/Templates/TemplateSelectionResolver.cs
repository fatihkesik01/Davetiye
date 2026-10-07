using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Davetiye.Infrastructure.Modules.Templates;

public sealed class TemplateSelectionResolver(DavetiyeDbContext dbContext) : ITemplateSelectionResolver
{
    public async Task<TemplateSelection?> ResolveActiveAsync(
        string templateKey,
        CancellationToken cancellationToken)
    {
        var normalizedKey = templateKey.Trim();

        var template = await dbContext.TemplateDefinitions
            .AsNoTracking()
            .Where(template => template.Key == normalizedKey && template.IsActive)
            .Select(template => new
            {
                template.Key,
                template.CurrentRendererVersion,
                template.IsPremium,
                template.SupportedModules
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (template is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(template.SupportedModules);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var modules = document.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new TemplateSelection(
                template.Key, template.CurrentRendererVersion, template.IsPremium, modules);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
