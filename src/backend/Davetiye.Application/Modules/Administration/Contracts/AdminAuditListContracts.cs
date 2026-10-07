namespace Davetiye.Application.Modules.Administration.Contracts;

public sealed record AdminAuditListItem(
    Guid Id,
    Guid ActorId,
    Guid SubjectId,
    DateTimeOffset OccurredAtUtc,
    string EventType);

public sealed record AdminAuditPage(
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<AdminAuditListItem> Items);

public interface IAdminAuditListReader
{
    Task<AdminAuditPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
