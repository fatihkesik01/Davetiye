using System.Text.Json.Serialization;

namespace Davetiye.Application.Modules.Invitations.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicationRevisions(
    long InvitationRevision,
    long WorkingContentRevision,
    long? PublishedContentRevision,
    Guid? WindowId,
    long? WindowRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicationWindowRequest(
    string Mode,
    string? StartsAtLocal,
    string EndsAtLocal,
    string TimeZoneId,
    Guid? RequestedGrantId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicationActionRequest(
    string Action,
    PublicationRevisions Expected,
    PublicationWindowRequest? Publication = null,
    bool PublishWorkingContent = false,
    bool ProceedWithRecommendedWarnings = false);

public sealed record PublicationWindowStatus(
    Guid Id, Guid GrantId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    string TimeZoneId, long Revision);

public sealed record PublishedSnapshotStatus(
    long Revision, long SourceWorkingRevision, string TemplateKey, int RendererVersion);

public sealed record PublicationPreflightStatus(
    bool TemplateAvailable, bool IsPremiumTemplate,
    IReadOnlyList<string> MissingRequiredFields, IReadOnlyList<string> MissingRecommendedFields);

public sealed record PublicationEntitlementStatus(
    long MaxPublishDays, long MaxActiveInvitations, bool PremiumTemplatesEnabled);

public sealed record PublicationGrantChoice(
    Guid? GrantId, string Label, string Kind, long MaxPublishDays,
    long MaxActiveInvitations, bool PremiumTemplatesEnabled);

public sealed record PublicationStatus(
    Guid InvitationId, string PublicCode, string StoredState, string EffectiveState,
    DateTimeOffset ServerNowUtc, string TimeZoneId, PublicationRevisions Expected,
    PublicationWindowStatus? CurrentWindow, PublishedSnapshotStatus? Published,
    bool HasPendingChanges, IReadOnlyList<string> AllowedActions,
    PublicationPreflightStatus Preflight, IReadOnlyList<PublicationGrantChoice> GrantChoices,
    PublicationEntitlementStatus? CurrentEntitlements,
    int? TrashRetentionDays = null);

public sealed record PublicationLifecycleResult(
    string Code,
    PublicationStatus? Status = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyList<string>? MissingRequiredFields = null,
    IReadOnlyList<string>? MissingRecommendedFields = null,
    PublicationRevisions? CurrentExpected = null);

public interface IPublicationLifecycleService
{
    Task<PublicationLifecycleResult> GetAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<PublicationLifecycleResult> ExecuteAsync(Guid accountId, Guid invitationId,
        PublicationActionRequest request, CancellationToken cancellationToken);
}
