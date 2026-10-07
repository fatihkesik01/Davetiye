namespace Davetiye.Application.Modules.Rsvp.Contracts;

public enum CreatorRsvpRateLimitBucket
{
    Read,
    Write
}

/// <summary>Account-keyed Creator RSVP limit, applied after the authenticated identity resolves to its account.</summary>
public interface ICreatorRsvpRateLimiter
{
    bool TryAcquire(Guid accountId, CreatorRsvpRateLimitBucket bucket);
}
