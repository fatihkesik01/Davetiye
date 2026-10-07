using Davetiye.Domain.Modules.Invitations;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class InvitationEffectiveStateEvaluatorTests
{
    private static readonly DateTimeOffset StartsAt = DateTimeOffset.Parse("2026-10-06T10:00:00Z");
    private static readonly DateTimeOffset EndsAt = DateTimeOffset.Parse("2026-10-06T12:00:00Z");

    [Theory]
    [InlineData(InvitationStoredState.Draft, false, "2026-10-06T09:00:00Z", InvitationStoredState.Draft)]
    [InlineData(InvitationStoredState.Scheduled, true, "2026-10-06T09:59:59Z", InvitationStoredState.Scheduled)]
    [InlineData(InvitationStoredState.Scheduled, true, "2026-10-06T10:00:00Z", InvitationStoredState.Active)]
    [InlineData(InvitationStoredState.Active, true, "2026-10-06T11:59:59Z", InvitationStoredState.Active)]
    [InlineData(InvitationStoredState.Paused, true, "2026-10-06T11:59:59Z", InvitationStoredState.Paused)]
    [InlineData(InvitationStoredState.Paused, true, "2026-10-06T12:00:00Z", InvitationStoredState.Expired)]
    [InlineData(InvitationStoredState.Expired, false, "2026-10-06T09:00:00Z", InvitationStoredState.Expired)]
    public void Minimal_projection_matches_entity_evaluation(
        InvitationStoredState storedState,
        bool hasWindow,
        string nowValue,
        InvitationStoredState expected)
    {
        var now = DateTimeOffset.Parse(nowValue);
        var window = hasWindow
            ? PublicationWindow.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StartsAt, EndsAt,
                "America/New_York", StartsAt.AddMinutes(-5))
            : null;

        var fromEntity = InvitationEffectiveStateEvaluator.Evaluate(storedState, window, now);
        var fromProjection = InvitationEffectiveStateEvaluator.Evaluate(
            storedState,
            hasWindow,
            window?.StartsAt,
            window?.EndsAt,
            now);

        Assert.Equal(expected, fromEntity);
        Assert.Equal(fromEntity, fromProjection);
    }
}
