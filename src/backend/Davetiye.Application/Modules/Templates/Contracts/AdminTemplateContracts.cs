namespace Davetiye.Application.Modules.Templates.Contracts;

/// <summary>Safe Admin projection of the editable template catalog metadata.</summary>
public sealed record AdminTemplateItem(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsActive,
    long Revision);

public sealed record AdminTemplateUpdateRequest(
    long ExpectedRevision,
    string Name,
    string? Description,
    bool IsActive);

public enum AdminTemplateUpdateOutcome
{
    Succeeded,
    NotFound,
    Conflict,
    InvalidRequest
}

public sealed record AdminTemplateUpdateResult(
    AdminTemplateUpdateOutcome Outcome,
    AdminTemplateItem? Template = null);

public interface IAdminTemplateService
{
    Task<IReadOnlyList<AdminTemplateItem>> ListAsync(CancellationToken cancellationToken);

    Task<AdminTemplateUpdateResult> UpdateAsync(
        Guid actorId,
        Guid templateId,
        AdminTemplateUpdateRequest request,
        CancellationToken cancellationToken);
}
