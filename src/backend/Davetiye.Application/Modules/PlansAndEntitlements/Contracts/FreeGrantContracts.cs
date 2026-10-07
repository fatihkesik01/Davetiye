namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

public enum FreeGrantReservationOutcome
{
    Reserved,
    AccountNotFound,
    AccountNotVerified,
    AccountInactive,
    InvitationNotFoundOrNotOwned,
    FreePlanUnavailable,
    InvalidConfiguration,
    ReservationNotFound,
    ReservedForAnotherInvitation,
    AlreadyConsumed,
    Revoked
}

public sealed record FreeGrantReservationResult(
    FreeGrantReservationOutcome Outcome,
    Guid? GrantId)
{
    public bool IsSuccess => Outcome == FreeGrantReservationOutcome.Reserved;
}

public interface IFreeGrantReservationStore
{
    /// <summary>
    /// Atomically gets or creates the account's lifetime Free row and reserves it. Concurrent calls
    /// for different invitations must have exactly one winner.
    /// </summary>
    Task<FreeGrantReservationResult> TryReserveAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken);

    /// <summary>Atomically reserves and consumes the Free right for immediate publication.</summary>
    Task<FreeGrantReservationResult> TryConsumeAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consumes a matching scheduled reservation at its authoritative start transition. It must
    /// never create or implicitly reserve a missing Free grant.
    /// </summary>
    Task<FreeGrantReservationResult> TryConsumeExistingReservationAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

}

public interface IFreePublicationGrantService
{
    Task<FreeGrantReservationResult> ReserveAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken);

    Task<FreeGrantReservationResult> ConsumeImmediatelyAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

    Task<FreeGrantReservationResult> ConsumeReservedAtStartAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

}
