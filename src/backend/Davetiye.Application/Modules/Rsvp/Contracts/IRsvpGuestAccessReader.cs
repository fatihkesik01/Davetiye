namespace Davetiye.Application.Modules.Rsvp.Contracts;

/// <summary>Evaluates the effective invitation/module gate without exposing invitation entities.</summary>
public interface IRsvpGuestAccessReader
{
    Task<bool> CanAcceptRsvpAsync(Guid invitationId, DateTimeOffset at, CancellationToken cancellationToken);
}
