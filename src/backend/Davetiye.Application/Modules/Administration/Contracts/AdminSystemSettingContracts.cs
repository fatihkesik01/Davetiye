namespace Davetiye.Application.Modules.Administration.Contracts;

public sealed record AdminSystemSettingItem(
    string Key,
    string DisplayName,
    string Description,
    int Value,
    int Minimum,
    int Maximum,
    long Revision);

public sealed record AdminSystemSettingsResponse(IReadOnlyList<AdminSystemSettingItem> Items);

public sealed record AdminSystemSettingUpdateRequest(long ExpectedRevision, int Value);

public enum AdminSystemSettingUpdateOutcome { Succeeded, NotFound, Conflict, InvalidRequest }

public sealed record AdminSystemSettingUpdateResult(
    AdminSystemSettingUpdateOutcome Outcome,
    AdminSystemSettingItem? Setting = null);

public interface IAdminSystemSettingsService
{
    Task<AdminSystemSettingsResponse> ListAsync(CancellationToken cancellationToken);

    Task<AdminSystemSettingUpdateResult> UpdateAsync(
        Guid actorId,
        string key,
        AdminSystemSettingUpdateRequest request,
        CancellationToken cancellationToken);
}
