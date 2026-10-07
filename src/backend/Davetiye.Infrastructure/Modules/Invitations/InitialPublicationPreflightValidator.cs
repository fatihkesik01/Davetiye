using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InitialPublicationPreflightValidator(
    ITemplateCatalogService templateCatalogService,
    ITemplateRendererRegistry rendererRegistry) : IInitialPublicationPreflightValidator
{
    private static readonly JsonSerializerOptions ContentOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8
    };

    public async Task<PublicationPreflightResult> ValidateAsync(
        string templateKey,
        int rendererVersion,
        int contentSchemaVersion,
        string content,
        CancellationToken cancellationToken)
    {
        if (contentSchemaVersion != InvitationDraftContract.CurrentContentSchemaVersion ||
            rendererRegistry.Resolve(templateKey, rendererVersion) is null)
        {
            return Unavailable();
        }

        // Inactive templates cannot be newly selected, but their existing pinned Drafts must
        // remain editable and publishable. This is an internal pin resolution, not catalog
        // discovery; new-selection paths continue to use active-only resolution.
        var template = await templateCatalogService.ResolvePinnedAsync(templateKey, cancellationToken);
        if (template is null)
        {
            return Unavailable();
        }

        DraftContentInput? typedContent;
        try
        {
            typedContent = JsonSerializer.Deserialize<DraftContentInput>(content, ContentOptions);
        }
        catch (JsonException)
        {
            return Unavailable();
        }

        if (typedContent is null)
        {
            return Unavailable();
        }

        var required = template.RequiredFields
            .Where(field => !IsFieldPresent(field, typedContent))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var recommended = template.RecommendedFields
            .Where(field => !IsFieldPresent(field, typedContent))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new PublicationPreflightResult(
            TemplateAvailable: true,
            template.IsPremium,
            required,
            recommended);
    }

    private static PublicationPreflightResult Unavailable() =>
        new(false, false, [], []);

    private static bool IsFieldPresent(string field, DraftContentInput content) => field switch
    {
        "eventType" => HasText(content.EventType),
        "headline" => HasText(content.Headline),
        "hostNames" => content.HostNames?.Any(HasText) == true,
        "message" => HasText(content.Message),
        "startsAt" => content.StartsAt.HasValue,
        "timeZoneId" => HasText(content.TimeZoneId),
        "venue.name" => HasText(content.Venue?.Name),
        "venue.address" => HasText(content.Venue?.Address),
        "venue.mapUrl" => HasText(content.Venue?.MapUrl),
        "programItems" => content.ProgramItems?.Any(item => item is not null &&
            (HasText(item.Title) || HasText(item.Description) || item.StartsAt.HasValue)) == true,
        "contacts" => content.Contacts?.Any(item => item is not null && HasText(item.Name) && HasText(item.Phone)) == true,
        "announcement" => HasText(content.Announcement),
        "faqs" => content.Faqs?.Any(item => item is not null && HasText(item.Question) && HasText(item.Answer)) == true,
        "transportStops" => content.TransportStops?.Any(item => item is not null && HasText(item.Name)) == true,
        _ => false
    };

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
