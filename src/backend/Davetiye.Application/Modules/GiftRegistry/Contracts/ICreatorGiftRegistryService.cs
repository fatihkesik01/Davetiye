namespace Davetiye.Application.Modules.GiftRegistry.Contracts;

public interface ICreatorGiftRegistryService
{
    Task<CreatorGiftItemsResult> ListAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<CreatorGiftItemResult> CreateAsync(Guid accountId, Guid invitationId, CreateGiftItemRequest request, CancellationToken cancellationToken);
    Task<CreatorGiftItemResult> UpdateAsync(Guid accountId, Guid invitationId, Guid itemId, UpdateGiftItemRequest request, CancellationToken cancellationToken);
    Task<CreatorGiftItemResult> DeleteAsync(Guid accountId, Guid invitationId, Guid itemId, long expectedRevision, CancellationToken cancellationToken);
    Task<CreatorGiftItemsResult> ReorderAsync(Guid accountId, Guid invitationId, ReorderGiftItemsRequest request, CancellationToken cancellationToken);
    Task<CreatorGiftReservationsResult> ListReservationsAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<CreatorGiftRegistryOutcome> RemoveReservationAsync(Guid accountId, Guid invitationId, Guid reservationId, CancellationToken cancellationToken);
}

/// <summary>Invitation-module ownership and lock boundary used by Gift Registry mutations.</summary>
public interface IGiftCreatorInvitationAccessReader
{
    Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<bool> LockOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}

public interface IGiftGuestInvitationAccessReader
{
    Task<GiftGuestInvitationAccess?> ReadAsync(string publicCode, CancellationToken cancellationToken);
    Task<GiftGuestInvitationAccess?> LockAndReadAsync(string publicCode, CancellationToken cancellationToken);
}

public sealed record GiftGuestInvitationAccess(Guid InvitationId, Guid AccountId, Guid GrantId,
    string TemplateKey, DateTimeOffset WindowStartsAt, DateTimeOffset WindowEndsAt);

public interface IPublicGiftRegistryService
{
    Task<PublicGiftRegistryResult> GetAsync(string publicCode, CancellationToken cancellationToken);
    Task<PublicGiftGuestReservationsResult> ListMyReservationsAsync(string publicCode, string? sessionToken, CancellationToken cancellationToken);
    Task<PublicGiftReservationResult> ReserveAsync(string publicCode, ReservePublicGiftRequest request, string? sessionToken, CancellationToken cancellationToken);
    Task<PublicGiftRegistryOutcome> CancelAsync(string publicCode, Guid reservationId, string? sessionToken, CancellationToken cancellationToken);
}

public sealed record PublicGiftRegistryItem(Guid Id, string Name, int RequestedQuantity, int RemainingQuantity, int Ordinal);
public sealed record PublicGiftRegistry(IReadOnlyList<PublicGiftRegistryItem> Items);
public enum PublicGiftRegistryOutcome { Available, NotFound, Invalid }
public sealed record PublicGiftRegistryResult(PublicGiftRegistryOutcome Outcome, PublicGiftRegistry? Registry = null);
public sealed record ReservePublicGiftRequest(Guid ItemId, int Quantity, string? FullName, string? Email, string? Phone);
public sealed record PublicGiftReservationResult(PublicGiftRegistryOutcome Outcome, Guid? ReservationId = null, string? SessionToken = null, DateTimeOffset? WindowEndsAt = null, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record PublicGiftGuestReservation(Guid ReservationId, Guid ItemId, string ItemName, int Quantity);
public sealed record PublicGiftGuestReservationsResult(PublicGiftRegistryOutcome Outcome, IReadOnlyList<PublicGiftGuestReservation>? Reservations = null);
public sealed record CreatorGiftReservation(Guid Id, Guid ItemId, int Quantity, string GuestFullName, string? Email, string? Phone, DateTimeOffset CreatedAt);
public sealed record CreatorGiftReservationsResult(CreatorGiftRegistryOutcome Outcome, IReadOnlyList<CreatorGiftReservation>? Reservations = null);

public sealed record CreateGiftItemRequest(string? Name, int RequestedQuantity);
public sealed record UpdateGiftItemRequest(string? Name, int RequestedQuantity, long ExpectedRevision);
public sealed record GiftItemOrderEntry(Guid Id, long Revision);
public sealed record ReorderGiftItemsRequest(IReadOnlyList<GiftItemOrderEntry>? Items);
public sealed record CreatorGiftItem(Guid Id, string Name, int RequestedQuantity, int ReservedQuantity,
    int RemainingQuantity, int Ordinal, long Revision);

public enum CreatorGiftRegistryOutcome { Succeeded, NotFound, Invalid, Conflict }
public sealed record CreatorGiftItemsResult(CreatorGiftRegistryOutcome Outcome,
    IReadOnlyList<CreatorGiftItem>? Items = null, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record CreatorGiftItemResult(CreatorGiftRegistryOutcome Outcome,
    CreatorGiftItem? Item = null, IReadOnlyDictionary<string, string[]>? Errors = null, long? CurrentRevision = null);
