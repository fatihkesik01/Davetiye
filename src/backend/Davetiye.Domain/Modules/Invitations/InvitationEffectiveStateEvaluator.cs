namespace Davetiye.Domain.Modules.Invitations;

/// <summary>Pure clock/window evaluator shared by Creator commands and the later public gate.</summary>
public static class InvitationEffectiveStateEvaluator
{
    public static InvitationStoredState Evaluate(
        InvitationStoredState storedState,
        PublicationWindow? currentWindow,
        DateTimeOffset now) => Evaluate(
            storedState,
            currentWindow is { IsCurrent: true },
            currentWindow?.StartsAt,
            currentWindow?.EndsAt,
            now);

    /// <summary>
    /// Evaluates the same lifecycle from the minimal publication-window values. Read models can
    /// project these values without materializing an Invitation or PublicationWindow entity.
    /// </summary>
    public static InvitationStoredState Evaluate(
        InvitationStoredState storedState,
        bool hasCurrentWindow,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(storedState))
        {
            throw new ArgumentOutOfRangeException(nameof(storedState));
        }

        if (storedState is InvitationStoredState.Draft or InvitationStoredState.Expired)
        {
            return storedState;
        }

        if (!hasCurrentWindow || startsAt is null || endsAt is null)
        {
            throw new InvalidOperationException("A published stored state requires one current publication window.");
        }

        if (startsAt.Value.Offset != TimeSpan.Zero || endsAt.Value.Offset != TimeSpan.Zero || endsAt.Value <= startsAt.Value)
        {
            throw new ArgumentException("Current publication window must be a valid UTC interval.");
        }

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Effective-state evaluation requires a UTC instant.", nameof(now));
        }

        if (now >= endsAt.Value)
        {
            return InvitationStoredState.Expired;
        }

        return storedState switch
        {
            InvitationStoredState.Scheduled when now < startsAt.Value =>
                InvitationStoredState.Scheduled,
            InvitationStoredState.Scheduled => InvitationStoredState.Active,
            InvitationStoredState.Active when now >= startsAt.Value =>
                InvitationStoredState.Active,
            InvitationStoredState.Paused when now >= startsAt.Value =>
                InvitationStoredState.Paused,
            _ => throw new InvalidOperationException(
                "Stored invitation state is inconsistent with its current publication window.")
        };
    }
}
