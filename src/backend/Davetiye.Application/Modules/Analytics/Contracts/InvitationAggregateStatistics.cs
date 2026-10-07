namespace Davetiye.Application.Modules.Analytics.Contracts;

/// <summary>Invitation-scoped scalar metrics. This contract contains no guest or visitor data.</summary>
public sealed record InvitationAggregateStatistics(
    long TotalPageViews,
    long RsvpResponseCount,
    long ParticipantCountTotal,
    long MemoryCount,
    long ReadyMediaCount,
    long ActiveGiftReservationCount);
