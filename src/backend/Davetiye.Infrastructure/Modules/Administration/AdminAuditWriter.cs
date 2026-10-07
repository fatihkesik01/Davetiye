using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Infrastructure.Persistence;

namespace Davetiye.Infrastructure.Modules.Administration;

/// <summary>Adds a minimized audit entity to the shared unit of work without committing it.</summary>
public sealed class AdminAuditWriter(DavetiyeDbContext db) : IAdminAuditWriter
{
    public void Add(Guid actorId, DateTimeOffset occurredAtUtc, string eventType, Guid subjectId)
    {
        db.AdminAuditRecords.Add(AdminAuditRecord.Create(
            Guid.NewGuid(), actorId, occurredAtUtc, eventType, subjectId));
    }
}
