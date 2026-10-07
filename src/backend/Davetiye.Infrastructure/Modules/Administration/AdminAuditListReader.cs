using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

public sealed class AdminAuditListReader(DavetiyeDbContext db) : IAdminAuditListReader
{
    public async Task<AdminAuditPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = GetOffset(page, pageSize);
        var query = db.AdminAuditRecords.AsNoTracking();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(record => record.OccurredAtUtc)
            .ThenByDescending(record => record.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(record => new AdminAuditListItem(
                record.Id,
                record.ActorId,
                record.SubjectId,
                record.OccurredAtUtc,
                record.EventType))
            .ToListAsync(cancellationToken);

        return new AdminAuditPage(page, pageSize, totalCount, items);
    }

    private static int GetOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page is outside the supported range.");

        return (page - 1) * pageSize;
    }
}
