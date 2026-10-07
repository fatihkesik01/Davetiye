namespace Davetiye.Application.Modules.Invitations.Contracts;


public sealed record PublicInvitationVenue(string? Name, string? Address);
public sealed record PublicInvitationProgramItem(string? Title, string? Description, DateTimeOffset? StartsAt);
public sealed record PublicInvitationContact(string Name, string? Role, string Phone);
public sealed record PublicInvitationFaq(string Question, string Answer);
public sealed record PublicInvitationTransportStop(string Name, string? Address, string? DepartureTime);
public sealed record PublicSnapshotMediaPlacement(Guid AssetId, string Kind, string Role, int SortOrder);
public sealed record PublicInvitationContent(
    string? EventType, string? Headline, IReadOnlyList<string>? HostNames, string? Message,
    DateTimeOffset? StartsAt, string TimeZoneId, PublicInvitationVenue? Venue,
    IReadOnlyList<PublicInvitationProgramItem>? ProgramItems,
    IReadOnlyList<PublicInvitationContact>? Contacts = null, string? Announcement = null,
    IReadOnlyList<PublicInvitationFaq>? Faqs = null, IReadOnlyList<PublicInvitationTransportStop>? TransportStops = null);

public sealed record PublicInvitationActive(
    string Status, string TemplateKey, int RendererVersion, int ContentSchemaVersion,
    PublicInvitationContent Content, IReadOnlyList<PublicSnapshotMediaPlacement>? Media = null);
public sealed record PublicInvitationUnavailable(string Status = "unavailable");
public sealed record PublicInvitationCapabilities(string CanonicalBaseUrl, bool MapEmbedEnabled,
    string MapEmbedOrigin, string MapEmbedPath, string? MapEmbedKey);
public sealed record InvitationStatistics(
    long TotalPageViews,
    long RsvpResponseCount,
    long ParticipantCountTotal,
    long MemoryCount,
    long ReadyMediaCount,
    long ActiveGiftReservationCount);

public enum PublicInvitationOutcome { Active, Unavailable, NotFound }
public sealed record PublicInvitationReadResult(PublicInvitationOutcome Outcome, PublicInvitationActive? Invitation = null,
    [property: System.Text.Json.Serialization.JsonIgnore] Guid? InvitationId = null);

public interface IPublicInvitationService
{
    Task<PublicInvitationReadResult> GetAsync(string publicCode, CancellationToken cancellationToken);
}
