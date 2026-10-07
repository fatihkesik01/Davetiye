namespace Davetiye.Domain.Modules.Invitations;

/// <summary>
/// Immutable publication entitlement window. The half-open interval and its GrantId remain an
/// audit-stable record even after a later reactivation creates another window.
/// </summary>
public sealed class PublicationWindow
{
    private PublicationWindow()
    {
    }

    public Guid Id { get; private set; }

    public Guid InvitationId { get; private set; }

    public Guid GrantId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public string TimeZoneId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsCurrent { get; private set; }

    public long Revision { get; private set; }

    public static PublicationWindow Create(
        Guid id,
        Guid invitationId,
        Guid grantId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string timeZoneId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Publication window id must not be empty.", nameof(id));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Publication window must reference an invitation.", nameof(invitationId));
        }

        if (grantId == Guid.Empty)
        {
            throw new ArgumentException("Publication window must permanently reference a grant.", nameof(grantId));
        }

        EnsureUtc(startsAt, nameof(startsAt));
        EnsureUtc(endsAt, nameof(endsAt));
        EnsureUtc(createdAt, nameof(createdAt));

        if (endsAt <= startsAt)
        {
            throw new ArgumentException("Publication window end must be after its start.", nameof(endsAt));
        }

        // This is deliberately only a storage/input-shape invariant. The Application boundary
        // resolves the identifier through IIanaTimeZoneValidator before creating this entity.
        EnsureIanaShape(timeZoneId);

        return new PublicationWindow
        {
            Id = id,
            InvitationId = invitationId,
            GrantId = grantId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            TimeZoneId = timeZoneId.Trim(),
            CreatedAt = createdAt,
            IsCurrent = true
        };
    }

    public void MarkHistorical()
    {
        if (!IsCurrent)
        {
            return;
        }

        IsCurrent = false;
        Revision++;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Publication instants must be expressed in UTC.", parameterName);
        }
    }

    private static void EnsureIanaShape(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100)
        {
            throw new ArgumentException(
                "An IANA time-zone id between 1 and 100 characters is required.", nameof(timeZoneId));
        }

        var normalized = timeZoneId.Trim();
        if (normalized.StartsWith('/') ||
            normalized.EndsWith('/') ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Any(character =>
                !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or
                    '/' or '_' or '-' or '+' or '.')))
        {
            throw new ArgumentException("Time-zone id must use a safe IANA identifier shape.", nameof(timeZoneId));
        }
    }
}
