using Davetiye.Domain.Modules.PlansAndEntitlements;

namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

public sealed record EntitlementResolutionContext(
    Guid AccountId,
    Guid GrantId,
    Guid InvitationId,
    PublicationEntitlementAction Action);

public enum PublicationEntitlementAction
{
    PublishNow,
    Schedule,
    Reschedule,
    Reactivate,
    Resume,
    UpdatePublishedContent,
    CreatorMediaUpload,
    RsvpSubmission,
    MemorySubmission,
    GiftReservation
}

public enum EntitlementResolutionDenial
{
    None,
    GrantNotFoundOrNotOwned,
    GrantNotEffective,
    AssignedToAnotherInvitation,
    ActionNotAllowedByGrantState,
    InvalidConfiguration
}

public sealed record EntitlementResolutionResult(
    EffectiveEntitlementSnapshot? Entitlements,
    EntitlementResolutionDenial Denial)
{
    public bool IsGranted => Entitlements is not null && Denial == EntitlementResolutionDenial.None;

    public static EntitlementResolutionResult Granted(EffectiveEntitlementSnapshot entitlements) =>
        new(entitlements, EntitlementResolutionDenial.None);

    public static EntitlementResolutionResult Denied(EntitlementResolutionDenial denial) =>
        new(null, denial);
}

public sealed record EffectiveEntitlementSnapshot(
    Guid AccountId,
    Guid GrantId,
    Guid PlanId,
    string PlanKey,
    PlanBillingKind PlanBillingKind,
    GrantSource GrantSource,
    Guid? AssignedInvitationId,
    DateTimeOffset? ReservedAt,
    DateTimeOffset? ConsumedAt,
    long MaxPublishDays,
    long MaxActiveInvitations,
    long MaxImages,
    long MaxVideos,
    long MaxImageSizeMb,
    long MaxVideoSizeMb,
    long MaxVideoDurationSeconds,
    long MaxRsvpResponses,
    bool MemoriesEnabled,
    bool GiftRegistryEnabled,
    bool PremiumTemplatesEnabled,
    long MaxGuestImages = 0,
    long MaxGuestVideos = 0,
    long MaxGuestImageSizeMb = 0,
    long MaxGuestVideoSizeMb = 0,
    long MaxGuestVideoDurationSeconds = 0);

public sealed record PlanEntitlementValueSnapshot(
    string Key,
    long? NumericValue,
    bool? BooleanValue);

/// <summary>One grant and its one plan; resolver implementations never merge values across rows.</summary>
public sealed record EntitlementGrantSnapshot(
    Guid AccountId,
    Guid GrantId,
    Guid PlanId,
    string PlanKey,
    bool PlanIsActive,
    PlanBillingKind PlanBillingKind,
    GrantSource GrantSource,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt,
    Guid? AssignedInvitationId,
    DateTimeOffset? ReservedAt,
    DateTimeOffset? ConsumedAt,
    IReadOnlyCollection<PlanEntitlementValueSnapshot> Values,
    DateTimeOffset? OrganizationSubscriptionPaidThroughAtUtc = null);

public interface IEntitlementGrantReader
{
    Task<EntitlementGrantSnapshot?> FindOwnedGrantAsync(
        Guid accountId,
        Guid grantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Plans-owned read port for the time boundary of an Organization-sourced grant. The contract
/// exposes no Payments entity or provider identity.
/// </summary>
public interface IOrganizationSubscriptionEntitlementReader
{
    Task<DateTimeOffset?> GetPaidThroughAtUtcAsync(
        Guid accountId,
        Guid planId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
        DateTimeOffset evaluatedAtUtc,
        int limit,
        CancellationToken cancellationToken);
}

public interface IEffectiveEntitlementResolver
{
    Task<EntitlementResolutionResult> ResolveAsync(
        EntitlementResolutionContext context,
        CancellationToken cancellationToken);
}
