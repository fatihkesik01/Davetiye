using System.Text.Json.Serialization;

namespace Davetiye.Application.Modules.Invitations.Contracts;

public static class InvitationDraftContract
{
    public const int CurrentContentSchemaVersion = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftContentInput
{
    public string? EventType { get; init; }

    public string? Headline { get; init; }

    public IReadOnlyList<string>? HostNames { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset? StartsAt { get; init; }

    public string? TimeZoneId { get; init; }

    public DraftVenueInput? Venue { get; init; }

    public IReadOnlyList<DraftProgramItemInput>? ProgramItems { get; init; }
    public IReadOnlyList<DraftContactInput>? Contacts { get; init; }
    public string? Announcement { get; init; }
    public IReadOnlyList<DraftFaqInput>? Faqs { get; init; }
    public IReadOnlyList<DraftTransportStopInput>? TransportStops { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftContactInput(string Name, string? Role, string Phone);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftFaqInput(string Question, string Answer);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftTransportStopInput(string Name, string? Address, string? DepartureTime);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftVenueInput
{
    public string? Name { get; init; }

    public string? Address { get; init; }

    public string? MapUrl { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftProgramItemInput
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset? StartsAt { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateInvitationDraftRequest(
    int ContentSchemaVersion,
    DraftContentInput? Content,
    string? TemplateKey);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutosaveInvitationDraftRequest(
    int ContentSchemaVersion,
    DraftContentInput Content,
    long ExpectedContentRevision);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SelectInvitationTemplateRequest(
    string TemplateKey,
    long ExpectedInvitationRevision);

public sealed record InvitationDraftSummary(
    Guid Id,
    string? TemplateKey,
    int? RendererVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long InvitationRevision,
    long ContentRevision,
    string EffectiveState = "Draft",
    string? Headline = null);

public sealed record InvitationDraftDetails(
    Guid Id,
    string? TemplateKey,
    int? RendererVersion,
    DateTimeOffset CreatedAt,
    long InvitationRevision,
    int ContentSchemaVersion,
    DraftContentInput Content,
    DateTimeOffset UpdatedAt,
    long ContentRevision);

public sealed record InvitationDraftPage(
    IReadOnlyList<InvitationDraftSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record InvitationDraftFieldValidation(
    string Field,
    bool IsRecognized,
    bool IsPresent);

public sealed record InvitationDraftValidationReport(
    Guid InvitationId,
    long InvitationRevision,
    long ContentRevision,
    string? TemplateKey,
    int? RendererVersion,
    bool TemplateSelected,
    bool TemplateAvailable,
    IReadOnlyList<InvitationDraftFieldValidation> RequiredFields,
    IReadOnlyList<InvitationDraftFieldValidation> RecommendedFields);

public enum InvitationDraftOutcome
{
    Succeeded,
    NotFound,
    Conflict,
    Invalid,
    TemplateUnavailable,
}

public sealed record InvitationDraftResult(
    InvitationDraftOutcome Outcome,
    InvitationDraftDetails? Draft = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentInvitationRevision = null,
    long? CurrentContentRevision = null);
