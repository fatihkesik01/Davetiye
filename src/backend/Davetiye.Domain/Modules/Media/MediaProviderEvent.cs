namespace Davetiye.Domain.Modules.Media;

/// <summary>Minimal replay marker for a verified provider webhook; raw payloads and secrets are not retained.</summary>
public sealed class MediaProviderEvent
{
    private MediaProviderEvent() { }

    public Guid Id { get; private set; }
    public string EventFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static MediaProviderEvent Create(Guid id, string eventFingerprint, DateTimeOffset receivedAt)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(eventFingerprint) || eventFingerprint.Length != 64 ||
            eventFingerprint.Any(character => !Uri.IsHexDigit(character)) || receivedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Media provider event metadata is invalid.");

        return new MediaProviderEvent
        {
            Id = id,
            EventFingerprint = eventFingerprint.ToLowerInvariant(),
            ReceivedAt = receivedAt
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        if (processedAt.Offset != TimeSpan.Zero || processedAt < ReceivedAt || ProcessedAt is not null)
            throw new ArgumentException("Media provider event processing timestamp is invalid.", nameof(processedAt));
        ProcessedAt = processedAt;
    }
}
