using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Domain.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Templates;

public sealed class AdminTemplateService(
    DavetiyeDbContext db,
    IAdminAuditWriter audit,
    IClock clock) : IAdminTemplateService
{
    public async Task<IReadOnlyList<AdminTemplateItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.TemplateDefinitions
            .AsNoTracking()
            .OrderBy(template => template.Key)
            .Select(template => new AdminTemplateItem(
                template.Id,
                template.Key,
                template.Name,
                template.Description,
                template.IsActive,
                template.Revision))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminTemplateUpdateResult> UpdateAsync(
        Guid actorId,
        Guid templateId,
        AdminTemplateUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || templateId == Guid.Empty || request.ExpectedRevision < 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > TemplateDefinition.NameMaxLength ||
            request.Description?.Trim().Length > TemplateDefinition.DescriptionMaxLength)
        {
            return new(AdminTemplateUpdateOutcome.InvalidRequest);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var template = await db.TemplateDefinitions
            .SingleOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);

        if (template is null)
        {
            return new(AdminTemplateUpdateOutcome.NotFound);
        }

        if (template.Revision != request.ExpectedRevision)
        {
            return new(AdminTemplateUpdateOutcome.Conflict);
        }

        try
        {
            var previousRevision = template.Revision;
            template.UpdateAdminMetadata(request.Name, request.Description, request.IsActive);
            if (template.Revision != previousRevision)
            {
                audit.Add(actorId, clock.UtcNow, "TemplateMetadataUpdated", template.Id);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new(AdminTemplateUpdateOutcome.Succeeded, ToItem(template));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminTemplateUpdateOutcome.Conflict);
        }
        catch (ArgumentException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminTemplateUpdateOutcome.InvalidRequest);
        }
    }

    private static AdminTemplateItem ToItem(TemplateDefinition template) =>
        new(template.Id, template.Key, template.Name, template.Description, template.IsActive, template.Revision);
}
