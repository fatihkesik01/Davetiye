namespace Davetiye.Application.Modules.Administration.Contracts;

/// <summary>Queues a minimal audit row in the caller's current transaction; persistence is committed by the command owner.</summary>
public interface IAdminAuditWriter
{
    void Add(Guid actorId, DateTimeOffset occurredAtUtc, string eventType, Guid subjectId);
}
