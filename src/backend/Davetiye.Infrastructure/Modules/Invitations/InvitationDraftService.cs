using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InvitationDraftService(
    DavetiyeDbContext dbContext,
    ITemplateSelectionResolver templateSelectionResolver,
    ITemplateCatalogService templateCatalogService,
    IPublicCodeGenerator publicCodeGenerator,
    ILogger<InvitationDraftService> logger,
    IClock clock) : IInvitationDraftService
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 8,
    };

    private static readonly Action<ILogger, Guid, long, long, Exception?> LogDraftCreated =
        LoggerMessage.Define<Guid, long, long>(
            LogLevel.Information,
            new EventId(2100, nameof(LogDraftCreated)),
            "Invitation draft created. InvitationId={InvitationId} InvitationRevision={InvitationRevision} ContentRevision={ContentRevision}");

    private static readonly Action<ILogger, Guid, long, Exception?> LogDraftAutosaved =
        LoggerMessage.Define<Guid, long>(
            LogLevel.Information,
            new EventId(2101, nameof(LogDraftAutosaved)),
            "Invitation draft autosaved. InvitationId={InvitationId} ContentRevision={ContentRevision}");

    private static readonly Action<ILogger, Guid, string, int, long, Exception?> LogDraftTemplateSelected =
        LoggerMessage.Define<Guid, string, int, long>(
            LogLevel.Information,
            new EventId(2102, nameof(LogDraftTemplateSelected)),
            "Invitation draft template selected. InvitationId={InvitationId} TemplateKey={TemplateKey} RendererVersion={RendererVersion} InvitationRevision={InvitationRevision}");

    public async Task<InvitationDraftPage> ListAsync(
        Guid accountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Invitations
            .AsNoTracking()
            .Where(invitation => invitation.AccountId == accountId)
            .Join(
                dbContext.WorkingContents.AsNoTracking(),
                invitation => invitation.Id,
                content => content.InvitationId,
                (invitation, content) => new { Invitation = invitation, Content = content });

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.Content.UpdatedAt)
            .ThenBy(row => row.Invitation.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new
            {
                row.Invitation, row.Content,
                Window = dbContext.PublicationWindows.AsNoTracking().SingleOrDefault(
                    window => window.InvitationId == row.Invitation.Id && window.IsCurrent)
            })
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow.ToUniversalTime();
        var summaries = rows.Select(row => new InvitationDraftSummary(
                row.Invitation.Id,
                row.Invitation.TemplateKey,
                row.Invitation.RendererVersion,
                row.Invitation.CreatedAt,
                row.Content.UpdatedAt,
                row.Invitation.Revision,
                row.Content.Revision,
                InvitationEffectiveStateEvaluator.Evaluate(row.Invitation.State, row.Window, now).ToString(),
                DeserializeContent(row.Content.Content).Headline)).ToArray();

        return new InvitationDraftPage(summaries, page, pageSize, totalCount);
    }

    public async Task<InvitationDraftResult> CreateAsync(
        Guid accountId,
        CreateInvitationDraftRequest request,
        CancellationToken cancellationToken)
    {
        var content = request.Content ?? new DraftContentInput();
        var validation = ValidateAndSerializeContent(request.ContentSchemaVersion, content);
        if (validation.Errors.Count > 0)
        {
            return Invalid(validation.Errors);
        }

        TemplateSelection? templateSelection = null;
        if (request.TemplateKey is not null)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateKey) || request.TemplateKey.Length > 100)
            {
                return Invalid(SingleError("templateKey", "Template key must be between 1 and 100 characters."));
            }

            templateSelection = await templateSelectionResolver.ResolveActiveAsync(
                request.TemplateKey,
                cancellationToken);
            if (templateSelection is null)
            {
                return new InvitationDraftResult(InvitationDraftOutcome.TemplateUnavailable);
            }
        }

        const int maxPublicCodeAttempts = 5;
        for (var attempt = 0; attempt < maxPublicCodeAttempts; attempt++)
        {
            var now = clock.UtcNow;
            var invitation = Invitation.Create(
                Guid.NewGuid(),
                accountId,
                publicCodeGenerator.Generate(),
                now);
            if (templateSelection is not null)
            {
                // Premium is deliberately not checked here: Phase 2 permits premium selection while in
                // Draft. Entitlement is a publish-preflight concern.
                invitation.PinTemplate(templateSelection.TemplateKey, templateSelection.RendererVersion);
            }

            var workingContent = WorkingContent.Create(
                Guid.NewGuid(),
                invitation.Id,
                request.ContentSchemaVersion,
                validation.Json,
                now);

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.Invitations.Add(invitation);
            dbContext.WorkingContents.Add(workingContent);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                LogDraftCreated(logger, invitation.Id, invitation.Revision, workingContent.Revision, null);
                return Success(ToDetails(invitation, workingContent, content));
            }
            catch (DbUpdateException exception) when (IsPublicCodeCollision(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique invitation public code.");
    }

    public async Task<InvitationDraftResult> GetAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var row = await LoadOwnedDraftAsync(accountId, invitationId, tracking: false, cancellationToken);
        return row is null
            ? new InvitationDraftResult(InvitationDraftOutcome.NotFound)
            : Success(ToDetails(row.Invitation, row.Content, DeserializeContent(row.Content.Content)));
    }

    public async Task<InvitationDraftValidationReport?> GetValidationAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        // Resolve ownership before consulting the public catalog. A foreign or unknown invitation
        // therefore produces the same 404 without revealing whether a template key is active.
        var row = await LoadOwnedDraftAsync(accountId, invitationId, tracking: false, cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.Invitation.TemplateKey is null || row.Invitation.RendererVersion is null)
        {
            return new InvitationDraftValidationReport(
                row.Invitation.Id,
                row.Invitation.Revision,
                row.Content.Revision,
                null,
                null,
                TemplateSelected: false,
                TemplateAvailable: false,
                RequiredFields: [],
                RecommendedFields: []);
        }

        var template = await templateCatalogService.ResolvePinnedAsync(
            row.Invitation.TemplateKey,
            cancellationToken);
        if (template is null)
        {
            return new InvitationDraftValidationReport(
                row.Invitation.Id,
                row.Invitation.Revision,
                row.Content.Revision,
                row.Invitation.TemplateKey,
                row.Invitation.RendererVersion,
                TemplateSelected: true,
                TemplateAvailable: false,
                RequiredFields: [],
                RecommendedFields: []);
        }

        var content = DeserializeContent(row.Content.Content);
        return new InvitationDraftValidationReport(
            row.Invitation.Id,
            row.Invitation.Revision,
            row.Content.Revision,
            row.Invitation.TemplateKey,
            row.Invitation.RendererVersion,
            TemplateSelected: true,
            TemplateAvailable: true,
            RequiredFields: EvaluateFields(template.RequiredFields, content),
            RecommendedFields: EvaluateFields(template.RecommendedFields, content));
    }

    public async Task<InvitationDraftResult> AutosaveAsync(
        Guid accountId,
        Guid invitationId,
        AutosaveInvitationDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return Invalid(SingleError("content", "Draft content is required."));
        }

        if (request.ExpectedContentRevision < 0)
        {
            return Invalid(SingleError("expectedContentRevision", "Expected content revision must not be negative."));
        }

        var validation = ValidateAndSerializeContent(request.ContentSchemaVersion, request.Content);
        if (validation.Errors.Count > 0)
        {
            return Invalid(validation.Errors);
        }

        var row = await LoadOwnedDraftAsync(accountId, invitationId, tracking: true, cancellationToken);
        if (row is null)
        {
            return new InvitationDraftResult(InvitationDraftOutcome.NotFound);
        }

        if (row.Content.Revision != request.ExpectedContentRevision)
        {
            return Conflict(row.Invitation.Revision, row.Content.Revision);
        }

        row.Content.ReplaceContent(validation.Json, request.ContentSchemaVersion, clock.UtcNow);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrencyConflictAsync(accountId, invitationId, cancellationToken);
        }

        LogDraftAutosaved(logger, row.Invitation.Id, row.Content.Revision, null);

        return Success(ToDetails(row.Invitation, row.Content, request.Content));
    }

    public async Task<InvitationDraftResult> SelectTemplateAsync(
        Guid accountId,
        Guid invitationId,
        SelectInvitationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedInvitationRevision < 0)
        {
            return Invalid(SingleError(
                "expectedInvitationRevision",
                "Expected invitation revision must not be negative."));
        }

        if (string.IsNullOrWhiteSpace(request.TemplateKey) || request.TemplateKey.Length > 100)
        {
            return Invalid(SingleError("templateKey", "Template key must be between 1 and 100 characters."));
        }

        // Ownership is resolved before catalog lookup so a foreign/unknown Invitation is always the
        // same 404, regardless of whether the supplied template key exists.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (!await InitialPublicationStore.LockOwnedInvitationAndWorkingContentAsync(
                dbContext, accountId, invitationId, cancellationToken))
        {
            return new InvitationDraftResult(InvitationDraftOutcome.NotFound);
        }

        var row = await LoadOwnedDraftAsync(accountId, invitationId, tracking: true, cancellationToken);
        if (row is null)
        {
            return new InvitationDraftResult(InvitationDraftOutcome.NotFound);
        }

        await dbContext.Entry(row.Invitation).ReloadAsync(cancellationToken);
        await dbContext.Entry(row.Content).ReloadAsync(cancellationToken);

        if (row.Invitation.Revision != request.ExpectedInvitationRevision)
        {
            return Conflict(row.Invitation.Revision, row.Content.Revision);
        }

        var selection = await templateSelectionResolver.ResolveActiveAsync(request.TemplateKey, cancellationToken);
        if (selection is null)
        {
            return new InvitationDraftResult(InvitationDraftOutcome.TemplateUnavailable);
        }

        var window = await dbContext.PublicationWindows.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.InvitationId == invitationId && candidate.IsCurrent,
            cancellationToken);
        if ((row.Invitation.State is InvitationStoredState.Scheduled or InvitationStoredState.Active or
                InvitationStoredState.Paused && window is null) ||
            InvitationEffectiveStateEvaluator.Evaluate(
                row.Invitation.State, window, clock.UtcNow.ToUniversalTime()) == InvitationStoredState.Active)
        {
            return Invalid(SingleError("templateKey", "The template cannot be changed while the invitation is active."));
        }

        invitationPremiumSelectionIsAllowedInDraft(selection);
        row.Invitation.PinTemplate(selection.TemplateKey, selection.RendererVersion);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrencyConflictAsync(accountId, invitationId, cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        LogDraftTemplateSelected(
            logger,
            row.Invitation.Id,
            selection.TemplateKey,
            selection.RendererVersion,
            row.Invitation.Revision,
            null);

        return Success(ToDetails(
            row.Invitation,
            row.Content,
            DeserializeContent(row.Content.Content)));
    }

    private static void invitationPremiumSelectionIsAllowedInDraft(TemplateSelection selection)
    {
        // Intentionally touches the metadata so this is an explicit reviewed policy, not an omitted
        // check: selection.IsPremium is allowed for Draft. Phase 3 publish preflight owns blocking.
        _ = selection.IsPremium;
    }

    private async Task<OwnedDraft?> LoadOwnedDraftAsync(
        Guid accountId,
        Guid invitationId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var invitations = tracking
            ? dbContext.Invitations.AsQueryable()
            : dbContext.Invitations.AsNoTracking();
        var contents = tracking
            ? dbContext.WorkingContents.AsQueryable()
            : dbContext.WorkingContents.AsNoTracking();

        return await invitations
            .Where(invitation => invitation.Id == invitationId && invitation.AccountId == accountId)
            .Join(
                contents,
                invitation => invitation.Id,
                content => content.InvitationId,
                (invitation, content) => new OwnedDraft(invitation, content))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<InvitationDraftResult> ResolveConcurrencyConflictAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var current = await LoadOwnedDraftAsync(accountId, invitationId, tracking: false, cancellationToken);
        return current is null
            ? new InvitationDraftResult(InvitationDraftOutcome.NotFound)
            : Conflict(current.Invitation.Revision, current.Content.Revision);
    }

    private static ContentValidationResult ValidateAndSerializeContent(
        int contentSchemaVersion,
        DraftContentInput content)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (contentSchemaVersion != InvitationDraftContract.CurrentContentSchemaVersion)
        {
            AddError(errors, "contentSchemaVersion", "Only content schema version 1 is supported.");
        }

        ValidateOptionalText(errors, "eventType", content.EventType, 50);
        if (!string.IsNullOrWhiteSpace(content.EventType) &&
            !AllowedEventTypes.Contains(content.EventType.Trim()))
        {
            AddError(errors, "eventType", "Event type is not supported.");
        }

        ValidateOptionalText(errors, "headline", content.Headline, 200);
        ValidateOptionalText(errors, "message", content.Message, 4_000);
        ValidateOptionalText(errors, "timeZoneId", content.TimeZoneId, 100);

        if (content.HostNames is { Count: > 10 })
        {
            AddError(errors, "hostNames", "At most 10 host names are allowed.");
        }

        if (content.HostNames is not null)
        {
            for (var index = 0; index < content.HostNames.Count; index++)
            {
                ValidateOptionalText(errors, $"hostNames[{index}]", content.HostNames[index], 100, required: true);
            }
        }

        if (content.Venue is not null)
        {
            ValidateOptionalText(errors, "venue.name", content.Venue.Name, 200);
            ValidateOptionalText(errors, "venue.address", content.Venue.Address, 500);
            ValidateOptionalText(errors, "venue.mapUrl", content.Venue.MapUrl, 2_048);

            if (!string.IsNullOrWhiteSpace(content.Venue.MapUrl) &&
                (!Uri.TryCreate(content.Venue.MapUrl, UriKind.Absolute, out var mapUri) ||
                 (mapUri.Scheme != Uri.UriSchemeHttp && mapUri.Scheme != Uri.UriSchemeHttps)))
            {
                AddError(errors, "venue.mapUrl", "Map URL must use http or https.");
            }
        }

        if (content.ProgramItems is { Count: > 20 })
        {
            AddError(errors, "programItems", "At most 20 program items are allowed.");
        }

        if (content.ProgramItems is not null)
        {
            for (var index = 0; index < content.ProgramItems.Count; index++)
            {
                var item = content.ProgramItems[index];
                if (item is null)
                {
                    AddError(errors, $"programItems[{index}]", "Program item must not be null.");
                    continue;
                }

                ValidateOptionalText(errors, $"programItems[{index}].title", item.Title, 200);
                ValidateOptionalText(errors, $"programItems[{index}].description", item.Description, 1_000);
            }
        }

        ValidateOptionalText(errors, "announcement", content.Announcement, 4_000);
        if (content.Contacts is not null)
            for (var index = 0; index < content.Contacts.Count; index++)
            {
                var item = content.Contacts[index];
                if (item is null) { AddError(errors, $"contacts[{index}]", "Contact must not be null."); continue; }
                ValidateOptionalText(errors, $"contacts[{index}].name", item.Name, 100, required: true);
                ValidateOptionalText(errors, $"contacts[{index}].role", item.Role, 100);
                ValidateOptionalText(errors, $"contacts[{index}].phone", item.Phone, 40, required: true);
                if (item.Phone is not null && item.Phone.Any(character => !char.IsAsciiDigit(character) && character is not ('+' or '-' or '(' or ')' or ' ')))
                    AddError(errors, $"contacts[{index}].phone", "Phone must contain phone-number characters only.");
            }
        if (content.Faqs is not null)
            for (var index = 0; index < content.Faqs.Count; index++)
            {
                var item = content.Faqs[index];
                if (item is null) { AddError(errors, $"faqs[{index}]", "FAQ must not be null."); continue; }
                ValidateOptionalText(errors, $"faqs[{index}].question", item.Question, 300, required: true);
                ValidateOptionalText(errors, $"faqs[{index}].answer", item.Answer, 2_000, required: true);
            }
        if (content.TransportStops is not null)
            for (var index = 0; index < content.TransportStops.Count; index++)
            {
                var item = content.TransportStops[index];
                if (item is null) { AddError(errors, $"transportStops[{index}]", "Transport stop must not be null."); continue; }
                ValidateOptionalText(errors, $"transportStops[{index}].name", item.Name, 200, required: true);
                ValidateOptionalText(errors, $"transportStops[{index}].address", item.Address, 500);
                if (item.DepartureTime is not null && !TimeOnly.TryParseExact(item.DepartureTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    AddError(errors, $"transportStops[{index}].departureTime", "Departure time must use HH:mm.");
            }

        var json = JsonSerializer.Serialize(content, ContentJsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > 65_536)
        {
            AddError(errors, "content", "Draft content must not exceed 65536 UTF-8 bytes.");
        }

        return new ContentValidationResult(
            json,
            errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    private static void ValidateOptionalText(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maxLength,
        bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                AddError(errors, key, "Value must not be blank.");
            }

            return;
        }

        if (value.Length > maxLength)
        {
            AddError(errors, key, $"Value must not exceed {maxLength} characters.");
        }

        if (value.Any(char.IsControl))
        {
            AddError(errors, key, "Value must not contain control characters.");
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static IReadOnlyDictionary<string, string[]> SingleError(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };

    private static bool IsPublicCodeCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_invitations_public_code"
        };

    private static InvitationDraftResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(InvitationDraftOutcome.Invalid, Errors: errors);

    private static InvitationDraftResult Conflict(long invitationRevision, long contentRevision) =>
        new(
            InvitationDraftOutcome.Conflict,
            CurrentInvitationRevision: invitationRevision,
            CurrentContentRevision: contentRevision);

    private static InvitationDraftResult Success(InvitationDraftDetails draft) =>
        new(InvitationDraftOutcome.Succeeded, draft);

    private static InvitationDraftDetails ToDetails(
        Invitation invitation,
        WorkingContent content,
        DraftContentInput typedContent) =>
        new(
            invitation.Id,
            invitation.TemplateKey,
            invitation.RendererVersion,
            invitation.CreatedAt,
            invitation.Revision,
            content.ContentSchemaVersion,
            typedContent,
            content.UpdatedAt,
            content.Revision);

    private static DraftContentInput DeserializeContent(string json) =>
        JsonSerializer.Deserialize<DraftContentInput>(json, ContentJsonOptions)
        ?? new DraftContentInput();

    private static IReadOnlyList<InvitationDraftFieldValidation> EvaluateFields(
        IReadOnlyList<string> fields,
        DraftContentInput content) =>
        fields
            .Distinct(StringComparer.Ordinal)
            .Select(field => new InvitationDraftFieldValidation(
                field,
                IsFieldRecognized(field),
                IsFieldPresent(field, content)))
            .ToArray();

    private static bool IsFieldRecognized(string field) => field is
        "eventType" or
        "headline" or
        "hostNames" or
        "message" or
        "startsAt" or
        "timeZoneId" or
        "venue.name" or
        "venue.address" or
        "venue.mapUrl" or
        "programItems" or "contacts" or "announcement" or "faqs" or "transportStops";

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
        "programItems" => content.ProgramItems?.Any(IsMeaningfulProgramItem) == true,
        "contacts" => content.Contacts?.Any(item => item is not null && HasText(item.Name) && HasText(item.Phone)) == true,
        "announcement" => HasText(content.Announcement),
        "faqs" => content.Faqs?.Any(item => item is not null && HasText(item.Question) && HasText(item.Answer)) == true,
        "transportStops" => content.TransportStops?.Any(item => item is not null && HasText(item.Name)) == true,
        _ => false,
    };

    private static bool IsMeaningfulProgramItem(DraftProgramItemInput? item) =>
        item is not null &&
        (HasText(item.Title) || HasText(item.Description) || item.StartsAt.HasValue);

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.Ordinal)
    {
        "dugun",
        "nisan",
        "kina",
        "sunnet",
        "dogum-gunu",
        "baby-shower",
        "mezuniyet",
        "acilis-genel",
    };

    private sealed record OwnedDraft(Invitation Invitation, WorkingContent Content);

    private sealed record ContentValidationResult(
        string Json,
        IReadOnlyDictionary<string, string[]> Errors);
}
