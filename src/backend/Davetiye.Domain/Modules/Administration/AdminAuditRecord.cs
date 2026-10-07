namespace Davetiye.Domain.Modules.Administration;

/// <summary>
/// Minimized append-only record of a privileged administrative action. It stores identifiers and
/// a bounded event code only; private notes, request bodies, and provider payloads do not belong here.
/// Actor and subject IDs are intentionally scalar so audit history survives related record removal.
/// </summary>
public sealed class AdminAuditRecord
{
    public const int MaxEventTypeLength = 100;

    private AdminAuditRecord() { }

    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid SubjectId { get; private set; }

    public static AdminAuditRecord Create(
        Guid id,
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        string eventType,
        Guid subjectId)
    {
        if (id == Guid.Empty || actorId == Guid.Empty || subjectId == Guid.Empty)
            throw new ArgumentException("Audit record, actor, and subject identifiers must not be empty.");
        if (occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Audit timestamp must be UTC.", nameof(occurredAtUtc));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Audit event type is required.", nameof(eventType));

        var normalizedEventType = eventType.Trim();
        if (normalizedEventType.Length > MaxEventTypeLength)
            throw new ArgumentException($"Audit event type cannot exceed {MaxEventTypeLength} characters.", nameof(eventType));

        return new AdminAuditRecord
        {
            Id = id,
            ActorId = actorId,
            OccurredAtUtc = occurredAtUtc,
            EventType = normalizedEventType,
            SubjectId = subjectId
        };
    }
}
