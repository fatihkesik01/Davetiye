using System.Data;
using System.Text.Json;
using System.Globalization;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class PublicInvitationService(
    DavetiyeDbContext dbContext, IAccountReferenceValidator accounts,
    IPublicationGrantAccessValidator grants, ITemplateRendererRegistry rendererRegistry,
    IIanaTimeZoneValidator zones, IClock clock) : IPublicInvitationService
{
    private static readonly JsonSerializerOptions ContentOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 8 };

    public async Task<PublicInvitationReadResult> GetAsync(string publicCode, CancellationToken cancellationToken)
    {
        if (!PublicInvitationCode.IsValid(publicCode)) return new(PublicInvitationOutcome.NotFound);

        // Each public request obtains one fresh read snapshot. Nothing consumes a grant, rewrites
        // Scheduled state, updates statistics or touches WorkingContent on this surface.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var invitation = await dbContext.Invitations.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.PublicCode == publicCode, cancellationToken);
        if (invitation is null) return new(PublicInvitationOutcome.NotFound);

        var published = await dbContext.PublishedContents.AsNoTracking().SingleOrDefaultAsync(
            content => content.InvitationId == invitation.Id, cancellationToken);
        var window = await dbContext.PublicationWindows.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.InvitationId == invitation.Id && candidate.IsCurrent, cancellationToken);
        var accountStatus = await accounts.GetStatusAsync(invitation.AccountId, cancellationToken);
        if (accountStatus != AccountReferenceStatus.Verified || published is null || window is null)
            return Unavailable();

        var grant = await grants.ReadAsync(invitation.AccountId, window.GrantId, cancellationToken);
        if (rendererRegistry.Resolve(published.TemplateKey, published.RendererVersion) is null ||
            published.ContentSchemaVersion != InvitationDraftContract.CurrentContentSchemaVersion)
            return Unavailable();

        PublicInvitationContent? content;
        try
        {
            // Deserialize only the public allowlist; unknown/private fields have no output slot.
            // This deliberately does not reuse a raw JSON or Creator draft response contract.
            content = JsonSerializer.Deserialize<PublicInvitationContent>(published.Content, ContentOptions);
        }
        catch (JsonException)
        {
            return Unavailable();
        }

        if (content is null || content.HostNames?.Any(name => name is null) == true ||
            content.ProgramItems?.Any(item => item is null) == true ||
            content.Contacts?.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Phone) ||
                item.Phone.Any(character => !char.IsAsciiDigit(character) && character is not ('+' or '-' or '(' or ')' or ' '))) == true ||
            content.Faqs?.Any(item => item is null || string.IsNullOrWhiteSpace(item.Question) || string.IsNullOrWhiteSpace(item.Answer)) == true ||
            content.TransportStops?.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) ||
                item.DepartureTime is not null && !TimeOnly.TryParseExact(item.DepartureTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) == true) return Unavailable();
        var timeZoneId = zones.IsValid(content.TimeZoneId) ? content.TimeZoneId.Trim() : window.TimeZoneId;
        if (!zones.IsValid(timeZoneId)) return Unavailable();
        var projection = content with
        {
            TimeZoneId = timeZoneId,
            StartsAt = content.StartsAt?.ToUniversalTime(),
            ProgramItems = content.ProgramItems?.Select(item => item with
            {
                StartsAt = item.StartsAt?.ToUniversalTime()
            }).ToArray()
        };
        IReadOnlyList<PublicSnapshotMediaPlacement> media;
        try
        {
            media = JsonSerializer.Deserialize<PublicSnapshotMediaPlacement[]>(published.MediaPlacements, ContentOptions) ?? [];
            if (media.Any(item => item.AssetId == Guid.Empty || item.Kind is not "Image" and not "Video" ||
                item.Role is not "Cover" and not "Gallery" || item.SortOrder < 0)) return Unavailable();
        }
        catch (JsonException)
        {
            return Unavailable();
        }

        // No awaited gate reads follow this instant. The window and any future revocation use
        // exactly this final clock, even if a preceding database/port read waited past a boundary.
        var now = clock.UtcNow.ToUniversalTime();
        InvitationStoredState effective;
        try
        {
            effective = InvitationEffectiveStateEvaluator.Evaluate(invitation.State, window, now);
        }
        catch (InvalidOperationException)
        {
            return Unavailable();
        }

        if (effective != InvitationStoredState.Active ||
            !PublicationGrantAccessPolicy.IsAllowed(grant, invitation.AccountId, invitation.Id,
                window.GrantId, window.StartsAt, now)) return Unavailable();
        return new(PublicInvitationOutcome.Active, new PublicInvitationActive("active", published.TemplateKey,
            published.RendererVersion, published.ContentSchemaVersion, projection, media), invitation.Id);
    }

    private static PublicInvitationReadResult Unavailable() => new(PublicInvitationOutcome.Unavailable);
}
