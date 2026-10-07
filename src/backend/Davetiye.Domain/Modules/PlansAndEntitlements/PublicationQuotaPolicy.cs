namespace Davetiye.Domain.Modules.PlansAndEntitlements;

public readonly record struct PublicationInterval
{
    public PublicationInterval(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("Publication interval end must be after its start.", nameof(endsAt));
        }

        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; }

    public TimeSpan Duration => EndsAt - StartsAt;

    public bool Overlaps(PublicationInterval other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
}

public sealed record AccountPublicationSlot(
    Guid InvitationId,
    PublicationInterval Interval);

public enum PublicationQuotaDenial
{
    None,
    PublishDurationExceeded,
    ActiveInvitationQuotaExceeded
}

public sealed record PublicationQuotaDecision(
    bool IsAllowed,
    PublicationQuotaDenial Denial,
    // Maximum simultaneous existing invitations inside the requested interval (candidate excluded).
    long OverlappingInvitationCount)
{
    public static PublicationQuotaDecision Allowed(long overlappingInvitationCount) =>
        new(true, PublicationQuotaDenial.None, overlappingInvitationCount);

    public static PublicationQuotaDecision Denied(
        PublicationQuotaDenial denial,
        long overlappingInvitationCount) =>
        new(false, denial, overlappingInvitationCount);
}

/// <summary>
/// Pure half-open interval policy. The persistence adapter must invoke it only after acquiring the
/// account transaction lock and must insert the accepted window before committing.
/// </summary>
public static class PublicationQuotaPolicy
{
    public static PublicationQuotaDecision Evaluate(
        Guid invitationId,
        PublicationInterval requestedInterval,
        long maxPublishDays,
        long maxActiveInvitations,
        IEnumerable<AccountPublicationSlot> existingSlots,
        bool enforceDuration = true)
    {
        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty.", nameof(invitationId));
        }

        ArgumentNullException.ThrowIfNull(existingSlots);

        if (maxPublishDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPublishDays));
        }

        if (maxActiveInvitations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActiveInvitations));
        }

        if (enforceDuration && requestedInterval.Duration > TimeSpan.FromDays(maxPublishDays))
        {
            return PublicationQuotaDecision.Denied(PublicationQuotaDenial.PublishDurationExceeded, 0);
        }

        var events = existingSlots
            .Where(slot => slot.InvitationId != invitationId)
            .Where(slot => slot.Interval.Overlaps(requestedInterval))
            .SelectMany(slot => new[]
            {
                (At: slot.Interval.StartsAt > requestedInterval.StartsAt
                    ? slot.Interval.StartsAt : requestedInterval.StartsAt, slot.InvitationId, Delta: 1),
                (At: slot.Interval.EndsAt < requestedInterval.EndsAt
                    ? slot.Interval.EndsAt : requestedInterval.EndsAt, slot.InvitationId, Delta: -1)
            })
            .OrderBy(change => change.At)
            // Half-open windows release their slots before an adjacent window starts.
            .ThenBy(change => change.Delta);

        var activeIntervals = new Dictionary<Guid, int>();
        long peakExistingInvitations = 0;
        foreach (var change in events)
        {
            activeIntervals.TryGetValue(change.InvitationId, out var intervalCount);
            intervalCount += change.Delta;
            if (intervalCount == 0)
            {
                activeIntervals.Remove(change.InvitationId);
            }
            else
            {
                // Multiple rows for one invitation still consume only one concurrent slot.
                activeIntervals[change.InvitationId] = intervalCount;
            }

            peakExistingInvitations = Math.Max(peakExistingInvitations, activeIntervals.Count);
        }

        // The requested invitation occupies one additional slot throughout its interval.
        return peakExistingInvitations >= maxActiveInvitations
            ? PublicationQuotaDecision.Denied(PublicationQuotaDenial.ActiveInvitationQuotaExceeded, peakExistingInvitations)
            : PublicationQuotaDecision.Allowed(peakExistingInvitations);
    }
}
