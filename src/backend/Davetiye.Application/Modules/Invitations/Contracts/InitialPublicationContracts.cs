using Davetiye.Domain.Modules.Invitations;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Media;

namespace Davetiye.Application.Modules.Invitations.Contracts;

public static class InitialPublicationContract
{
    public const string DefaultTimeZoneId = "Europe/Istanbul";
}

public enum InitialPublicationMode
{
    Immediate,
    Scheduled
}

public sealed record InitialPublicationRequest(
    Guid? RequestedGrantId,
    InitialPublicationMode Mode,
    DateTimeOffset? ScheduledStartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string TimeZoneId,
    long ExpectedInvitationRevision,
    long ExpectedWorkingContentRevision,
    bool ProceedWithRecommendedWarnings);

public enum InitialPublicationOutcome
{
    Succeeded,
    NotFound,
    AccountInactive,
    Conflict,
    InvalidRequest,
    InvalidState,
    TemplateUnavailable,
    RequiredFieldsMissing,
    RecommendedFieldsRequireConfirmation,
    GrantUnavailable,
    PremiumTemplateNotAllowed,
    PublishDurationExceeded,
    ActiveInvitationQuotaExceeded
}

public sealed record InitialPublicationResult(
    InitialPublicationOutcome Outcome,
    Guid? InvitationId = null,
    string? PublicCode = null,
    InvitationStoredState? StoredState = null,
    DateTimeOffset? StartsAtUtc = null,
    DateTimeOffset? EndsAtUtc = null,
    IReadOnlyList<string>? MissingRequiredFields = null,
    IReadOnlyList<string>? MissingRecommendedFields = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentInvitationRevision = null,
    long? CurrentWorkingContentRevision = null);

public interface IInitialPublicationService
{
    Task<InitialPublicationResult> PublishAsync(
        Guid accountId,
        Guid invitationId,
        InitialPublicationRequest request,
        CancellationToken cancellationToken);
}

public sealed record InitialPublicationDraft(
    Invitation Invitation,
    WorkingContent WorkingContent,
    bool HasPublishedContent,
    bool HasCurrentPublicationWindow,
    IReadOnlyList<PublicSnapshotMediaPlacement>? MediaPlacements = null);

public enum InitialPublicationSaveOutcome
{
    Saved,
    Conflict
}

public sealed record InitialPublicationSaveResult(
    InitialPublicationSaveOutcome Outcome,
    long? CurrentInvitationRevision = null,
    long? CurrentWorkingContentRevision = null);

/// <summary>
/// Invitation-owned persistence boundary. Implementations use the same scoped DbContext as the
/// grant allocator so one final save/transaction commits grant, snapshot, window and state.
/// </summary>
public interface IInitialPublicationStore
{
    /// <summary>
    /// Performs one owner-scoped load and row-locks both Invitation and WorkingContent (FOR UPDATE)
    /// inside the account transaction. This is the serialization point against autosave/template
    /// writes and guarantees the PublishedContent copy is the exact accepted Working snapshot.
    /// </summary>
    Task<InitialPublicationDraft?> LoadOwnedDraftAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationQuotaSlot>> ListAccountPublicationSlotsAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<InitialPublicationSaveResult> SaveAsync(
        Invitation invitation,
        PublishedContent publishedContent,
        PublicationWindow publicationWindow,
        CancellationToken cancellationToken);
}

public sealed record PublicationPreflightResult(
    bool TemplateAvailable,
    bool IsPremiumTemplate,
    IReadOnlyList<string> MissingRequiredFields,
    IReadOnlyList<string> MissingRecommendedFields);

public interface IInitialPublicationPreflightValidator
{
    Task<PublicationPreflightResult> ValidateAsync(
        string templateKey,
        int rendererVersion,
        int contentSchemaVersion,
        string content,
        CancellationToken cancellationToken);
}

/// <summary>Resolves actual IANA identifiers; domain validation checks only safe storage shape.</summary>
public interface IIanaTimeZoneValidator
{
    bool IsValid(string timeZoneId);
}
