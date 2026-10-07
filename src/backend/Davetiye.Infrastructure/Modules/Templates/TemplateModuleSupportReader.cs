using System.Text.Json;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Templates;

public sealed class TemplateModuleSupportReader(DavetiyeDbContext dbContext) : ITemplateModuleSupportReader
{
    public async Task<bool> SupportsModuleAsync(string templateKey, string moduleKey, CancellationToken cancellationToken)
    {
        var modules = await dbContext.TemplateDefinitions.AsNoTracking().Where(item => item.Key == templateKey)
            .Select(item => item.SupportedModules).SingleOrDefaultAsync(cancellationToken);
        if (modules is null) return false;
        try
        {
            using var document = JsonDocument.Parse(modules);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
                       string.Equals(item.GetString(), moduleKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return false; }
    }
}
