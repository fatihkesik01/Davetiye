using System.Data;
using System.Text.Json;
using System.Globalization;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class PublicationLifecycleService(
    DavetiyeDbContext dbContext,
    IInitialPublicationService initialPublicationService,
    IAccountReferenceValidator accountValidator,
    IInitialPublicationPreflightValidator preflightValidator,
    IIanaTimeZoneValidator zoneValidator,
    IPublicationGrantAllocator allocator,
    IPublicationGrantLifecycleService grants,
    IEffectiveEntitlementResolver resolver,
    IAccountQuotaTransactionRunner runner,
    IClock clock,
    IInvitationRetentionSettingsReader retentionSettings,
    Davetiye.Application.Modules.Media.Contracts.IMediaPublicationSnapshotReader mediaSnapshots) : IPublicationLifecycleService
{
    private static readonly string[] Actions =
        ["publish", "update", "pause", "resume", "cancelSchedule", "reschedule", "publishNow", "reactivate", "republish"];

    public async Task<PublicationLifecycleResult> GetAsync(Guid accountId, Guid invitationId,
        CancellationToken cancellationToken)
    {
        // One read snapshot keeps state, Working, Published and window consistent without any
        // reconciliation writes or row locks on this GET surface.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var aggregate = await LoadAsync(accountId, invitationId, tracking: false, cancellationToken);
        if (aggregate is null)
        {
            return new("NotFound");
        }

        if (await accountValidator.GetStatusAsync(accountId, cancellationToken) != AccountReferenceStatus.Verified)
        {
            return new("AccountInactive");
        }

        return new("Succeeded", await ToStatusAsync(aggregate, clock.UtcNow.ToUniversalTime(), cancellationToken));
    }

    public async Task<PublicationLifecycleResult> ExecuteAsync(Guid accountId, Guid invitationId,
        PublicationActionRequest request, CancellationToken cancellationToken)
    {
        var error = ValidateActionRequest(request);
        if (error is not null)
        {
            return error;
        }

        try
        {
            return await runner.ExecuteAsync(accountId, async token =>
            {
                if (!await InitialPublicationStore.LockOwnedInvitationAndWorkingContentAsync(
                        dbContext, accountId, invitationId, token))
                {
                    return new PublicationLifecycleResult("NotFound");
                }

                var aggregate = (await LoadAsync(accountId, invitationId, tracking: true, token))!;
                var now = clock.UtcNow.ToUniversalTime();
                if (await accountValidator.GetStatusAsync(accountId, token) != AccountReferenceStatus.Verified)
                {
                    return new PublicationLifecycleResult("AccountInactive");
                }

                var expected = Revisions(aggregate);
                if (request.Expected != expected)
                {
                    return new PublicationLifecycleResult("Conflict", CurrentExpected: expected);
                }

                var effectiveState = EffectiveState(aggregate, now);
                if (!AllowedActions(aggregate, effectiveState, now).Contains(request.Action, StringComparer.Ordinal))
                {
                    return new PublicationLifecycleResult("InvalidState", CurrentExpected: expected);
                }

                // The clock is authoritative even if a Scheduled window started and ended while
                // no worker ran. Compare revisions first; consuming this grant never changes the
                // invitation/window snapshot that the caller just accepted.
                if (aggregate.Window is not null && aggregate.Window.StartsAt <= now)
                {
                    var consumed = await grants.ConsumeStartedAsync(accountId, invitationId,
                        aggregate.Window.GrantId, aggregate.Window.StartsAt, token);
                    if (!consumed && request.Action is not "pause" and not "reactivate" &&
                        !(request.Action == "publish" && aggregate.Window.EndsAt <= now))
                    {
                        Abort("GrantUnavailable");
                    }
                }

                now = clock.UtcNow.ToUniversalTime();
                effectiveState = EffectiveState(aggregate, now);
                if (!AllowedActions(aggregate, effectiveState, now).Contains(request.Action, StringComparer.Ordinal))
                    Abort(new PublicationLifecycleResult("InvalidState", CurrentExpected: expected));

                if (request.Action == "publish")
                {
                    if (aggregate.Window is not null && aggregate.Window.EndsAt <= now)
                    {
                        aggregate.Window.MarkHistorical();
                        await dbContext.SaveChangesAsync(token);
                    }

                    var times = ResolveWindow(request.Publication!, now);
                    if (times.Error is not null)
                    {
                        Abort(times.Error);
                    }

                    var result = await initialPublicationService.PublishAsync(accountId, invitationId,
                        new InitialPublicationRequest(request.Publication!.RequestedGrantId,
                            times.Scheduled ? InitialPublicationMode.Scheduled : InitialPublicationMode.Immediate,
                            times.Scheduled ? times.StartsAt : null, times.EndsAt, request.Publication.TimeZoneId,
                            request.Expected.InvitationRevision, request.Expected.WorkingContentRevision,
                            request.ProceedWithRecommendedWarnings), token);
                    if (result.Outcome != InitialPublicationOutcome.Succeeded)
                    {
                        Abort(new PublicationLifecycleResult(result.Outcome.ToString(), Errors: result.Errors,
                            MissingRequiredFields: result.MissingRequiredFields,
                            MissingRecommendedFields: result.MissingRecommendedFields,
                            CurrentExpected: expected));
                    }
                }
                else
                {
                    await ApplyAsync(aggregate, request, effectiveState, now, token);
                }

                var refreshed = (await LoadAsync(accountId, invitationId, tracking: false, token))!;
                return new PublicationLifecycleResult("Succeeded", await ToStatusAsync(refreshed,
                    clock.UtcNow.ToUniversalTime(), token));
            }, cancellationToken);
        }
        catch (LifecycleAbort exception)
        {
            return exception.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new("Conflict");
        }
    }

    private async Task ApplyAsync(Aggregate aggregate, PublicationActionRequest request,
        InvitationStoredState effectiveState, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var invitation = aggregate.Invitation;
        var window = aggregate.Window;
        switch (request.Action)
        {
            case "pause":
                invitation.ChangePublicationState(InvitationStoredState.Paused);
                break;
            case "cancelSchedule":
                if (!await grants.ReleaseAsync(invitation.AccountId, invitation.Id, window!.GrantId, cancellationToken))
                {
                    Abort("GrantUnavailable");
                }

                window.MarkHistorical();
                invitation.ChangePublicationState(InvitationStoredState.Draft);
                break;
            case "update":
            case "resume":
                if (request.Action == "update" || request.PublishWorkingContent)
                {
                    var entitlements = await ResolveCurrentAsync(aggregate,
                        PublicationEntitlementAction.UpdatePublishedContent, cancellationToken);
                    await PromoteWorkingAsync(aggregate, entitlements, request, now, cancellationToken);
                }
                else
                {
                    // Keeping the already accepted Published/window ignores current numeric and
                    // premium downgrades; an explicit revoke always closes admission.
                    await ResolveCurrentAsync(aggregate, PublicationEntitlementAction.Resume, cancellationToken);
                }

                invitation.ChangePublicationState(request.Action == "resume"
                    ? InvitationStoredState.Active : effectiveState);
                break;
            case "republish":
                var remaining = await ResolveCurrentAsync(aggregate,
                    request.PublishWorkingContent ? PublicationEntitlementAction.UpdatePublishedContent
                        : PublicationEntitlementAction.Resume, cancellationToken);
                if (aggregate.Published is null) Abort("InvalidState");
                if (request.PublishWorkingContent)
                {
                    await PromoteWorkingAsync(aggregate, remaining, request, now, cancellationToken);
                }

                await CheckQuotaAsync(aggregate, now > window!.StartsAt ? now : window.StartsAt,
                    window.EndsAt, remaining, cancellationToken, enforceDuration: false);
                invitation.ChangePublicationState(now < window.StartsAt
                    ? InvitationStoredState.Scheduled : InvitationStoredState.Active);
                break;
            case "reschedule":
            case "publishNow":
                if (request.Action == "reschedule" &&
                    request.Publication!.RequestedGrantId is not null &&
                    request.Publication.RequestedGrantId != window!.GrantId)
                {
                    Abort(Invalid("requestedGrantId", "Rescheduling must use the accepted window's grant."));
                }

                var scheduled = request.Action == "reschedule";
                var times = scheduled ? ResolveWindow(request.Publication!, now)
                    : new WindowResolution(false, now, window!.EndsAt, null);
                if (times.Error is not null)
                {
                    Abort(times.Error);
                }

                var resolution = await resolver.ResolveAsync(new EntitlementResolutionContext(
                    invitation.AccountId, window!.GrantId, invitation.Id,
                    scheduled ? PublicationEntitlementAction.Reschedule : PublicationEntitlementAction.PublishNow), cancellationToken);
                if (!resolution.IsGranted)
                {
                    Abort("GrantUnavailable");
                }

                await ValidatePublishedAsync(aggregate, resolution.Entitlements!, request, cancellationToken);
                await CheckQuotaAsync(aggregate, times.StartsAt, times.EndsAt, resolution.Entitlements!, cancellationToken);
                if (!scheduled)
                {
                    var allocation = await allocator.AllocateAsync(new PublicationGrantAllocationRequest(
                        invitation.AccountId, invitation.Id, window.GrantId, PublicationEntitlementAction.PublishNow, now), cancellationToken);
                    if (!allocation.IsAllocated)
                    {
                        Abort("GrantUnavailable");
                    }
                }

                await ReplaceWindowAsync(aggregate, window.GrantId, times.StartsAt, times.EndsAt,
                    scheduled ? request.Publication!.TimeZoneId : window.TimeZoneId, now, cancellationToken);
                invitation.ChangePublicationState(scheduled ? InvitationStoredState.Scheduled : InvitationStoredState.Active);
                break;
            case "reactivate":
                var reactivation = ResolveWindow(request.Publication!, now);
                if (reactivation.Error is not null)
                {
                    Abort(reactivation.Error);
                }

                var startedGrantIds = await StartedGrantIdsAsync(invitation.AccountId, now, cancellationToken);
                if (request.Publication!.RequestedGrantId is not null &&
                    startedGrantIds.Contains(request.Publication.RequestedGrantId.Value))
                {
                    Abort("GrantUnavailable");
                }

                var allocated = await allocator.AllocateAsync(new PublicationGrantAllocationRequest(
                    invitation.AccountId, invitation.Id, request.Publication.RequestedGrantId,
                    reactivation.Scheduled ? PublicationEntitlementAction.Schedule : PublicationEntitlementAction.PublishNow,
                    now), cancellationToken);
                if (!allocated.IsAllocated || !PublicationAdmission.AllocationMatchesRequest(allocated.Entitlements!,
                        request.Publication.RequestedGrantId, invitation.AccountId, invitation.Id, reactivation.Scheduled))
                {
                    Abort("GrantUnavailable");
                }

                await PromoteWorkingAsync(aggregate, allocated.Entitlements!, request, now, cancellationToken);
                await CheckQuotaAsync(aggregate, reactivation.StartsAt, reactivation.EndsAt, allocated.Entitlements!, cancellationToken);
                await ReplaceWindowAsync(aggregate, allocated.Entitlements!.GrantId, reactivation.StartsAt,
                    reactivation.EndsAt, request.Publication.TimeZoneId, now, cancellationToken);
                invitation.ChangePublicationState(reactivation.Scheduled ? InvitationStoredState.Scheduled : InvitationStoredState.Active);
                break;
        }

        var finalNow = clock.UtcNow.ToUniversalTime();
        if (window is not null && request.Action is "update" or "resume" or "republish" or "pause" &&
            window.EndsAt <= finalNow)
            Abort("InvalidState");
        if (window is not null && request.Action is "cancelSchedule" or "reschedule" or "publishNow" &&
            window.StartsAt <= finalNow)
            Abort("InvalidState");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EffectiveEntitlementSnapshot> ResolveCurrentAsync(Aggregate aggregate,
        PublicationEntitlementAction action, CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(new EntitlementResolutionContext(aggregate.Invitation.AccountId,
            aggregate.Window!.GrantId, aggregate.Invitation.Id, action), cancellationToken);
        if (!result.IsGranted)
        {
            Abort("GrantUnavailable");
        }

        return result.Entitlements!;
    }

    private async Task PromoteWorkingAsync(Aggregate aggregate, EffectiveEntitlementSnapshot entitlements,
        PublicationActionRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // An Active invitation may update content, but cannot change its published template pin.
        if (EffectiveState(aggregate, now) == InvitationStoredState.Active && aggregate.Published is not null &&
            (aggregate.Invitation.TemplateKey != aggregate.Published.TemplateKey ||
             aggregate.Invitation.RendererVersion != aggregate.Published.RendererVersion))
        {
            Abort("InvalidState");
        }

        var preflight = await WorkingPreflightAsync(aggregate, cancellationToken);
        CheckPreflight(preflight, entitlements, request);
        var snapshot = PublishedContent.Create(Guid.NewGuid(), aggregate.Invitation.Id,
            aggregate.Invitation.TemplateKey!, aggregate.Invitation.RendererVersion!.Value,
            aggregate.Working.ContentSchemaVersion, aggregate.Working.Content, aggregate.Working.Revision, now,
            JsonSerializer.Serialize(await mediaSnapshots.ListReadyPlacementsAsync(aggregate.Invitation.Id, cancellationToken)));
        if (aggregate.Published is null)
        {
            dbContext.PublishedContents.Add(snapshot);
        }
        else
        {
            aggregate.Published.ReplaceWith(snapshot);
        }
    }

    private async Task ValidatePublishedAsync(Aggregate aggregate, EffectiveEntitlementSnapshot entitlements,
        PublicationActionRequest request, CancellationToken cancellationToken)
    {
        if (aggregate.Published is null)
        {
            Abort("InvalidState");
        }

        var published = aggregate.Published!;
        var preflight = await preflightValidator.ValidateAsync(published.TemplateKey, published.RendererVersion,
            published.ContentSchemaVersion, published.Content, cancellationToken);
        CheckPreflight(preflight, entitlements, request);
    }

    private static void CheckPreflight(PublicationPreflightResult preflight,
        EffectiveEntitlementSnapshot entitlements, PublicationActionRequest request)
    {
        if (!preflight.TemplateAvailable) Abort("TemplateUnavailable");
        if (preflight.MissingRequiredFields.Count > 0)
            Abort(new PublicationLifecycleResult("RequiredFieldsMissing", MissingRequiredFields: preflight.MissingRequiredFields));
        if (preflight.MissingRecommendedFields.Count > 0 && !request.ProceedWithRecommendedWarnings)
            Abort(new PublicationLifecycleResult("RecommendedFieldsRequireConfirmation", MissingRecommendedFields: preflight.MissingRecommendedFields));
        if (preflight.IsPremiumTemplate && !entitlements.PremiumTemplatesEnabled) Abort("PremiumTemplateNotAllowed");
    }

    private async Task CheckQuotaAsync(Aggregate aggregate, DateTimeOffset startsAt, DateTimeOffset endsAt,
        EffectiveEntitlementSnapshot entitlements, CancellationToken cancellationToken, bool enforceDuration = true)
    {
        var slots = await new InitialPublicationStore(dbContext, mediaSnapshots).ListAccountPublicationSlotsAsync(
            aggregate.Invitation.AccountId, cancellationToken);
        var decision = PublicationAdmission.EvaluateQuota(aggregate.Invitation.Id, startsAt, endsAt,
            entitlements.MaxPublishDays, entitlements.MaxActiveInvitations, slots, enforceDuration);
        if (!decision.IsAllowed)
        {
            Abort(decision.Denial == PublicationAdmissionDenial.PublishDurationExceeded
                ? "PublishDurationExceeded" : "ActiveInvitationQuotaExceeded");
        }
    }

    private async Task ReplaceWindowAsync(Aggregate aggregate, Guid grantId, DateTimeOffset startsAt,
        DateTimeOffset endsAt, string timeZoneId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        aggregate.Window?.MarkHistorical();
        // Retire the old filtered-unique row before inserting its replacement, inside the same
        // outer transaction. A later denial/failure rolls both saves back.
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.PublicationWindows.Add(PublicationWindow.Create(Guid.NewGuid(), aggregate.Invitation.Id,
            grantId, startsAt, endsAt, timeZoneId, now));
    }

    private async Task<Aggregate?> LoadAsync(Guid accountId, Guid invitationId, bool tracking,
        CancellationToken cancellationToken)
    {
        var invitation = await (tracking ? dbContext.Invitations.AsQueryable() : dbContext.Invitations.AsNoTracking())
            .SingleOrDefaultAsync(candidate => candidate.Id == invitationId && candidate.AccountId == accountId, cancellationToken);
        if (invitation is null) return null;
        var working = await (tracking ? dbContext.WorkingContents.AsQueryable() : dbContext.WorkingContents.AsNoTracking())
            .SingleAsync(content => content.InvitationId == invitationId, cancellationToken);
        var published = await (tracking ? dbContext.PublishedContents.AsQueryable() : dbContext.PublishedContents.AsNoTracking())
            .SingleOrDefaultAsync(content => content.InvitationId == invitationId, cancellationToken);
        var window = await (tracking ? dbContext.PublicationWindows.AsQueryable() : dbContext.PublicationWindows.AsNoTracking())
            .SingleOrDefaultAsync(candidate => candidate.InvitationId == invitationId && candidate.IsCurrent, cancellationToken);
        if (tracking)
        {
            await dbContext.Entry(invitation).ReloadAsync(cancellationToken);
            await dbContext.Entry(working).ReloadAsync(cancellationToken);
            if (published is not null) await dbContext.Entry(published).ReloadAsync(cancellationToken);
            if (window is not null) await dbContext.Entry(window).ReloadAsync(cancellationToken);
        }

        return new(invitation, working, published, window);
    }

    private async Task<PublicationStatus> ToStatusAsync(Aggregate aggregate, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = EffectiveState(aggregate, now);
        var preflight = await WorkingPreflightAsync(aggregate, cancellationToken);
        var started = await StartedGrantIdsAsync(aggregate.Invitation.AccountId, now, cancellationToken);
        var choices = await grants.ListChoicesAsync(aggregate.Invitation.AccountId, aggregate.Invitation.Id,
            started, cancellationToken);
        EffectiveEntitlementSnapshot? entitlements = null;
        if (aggregate.Window is not null)
        {
            var result = await resolver.ResolveAsync(new EntitlementResolutionContext(aggregate.Invitation.AccountId,
                aggregate.Window.GrantId, aggregate.Invitation.Id, PublicationEntitlementAction.Resume), cancellationToken);
            entitlements = result.Entitlements;
        }

        var window = aggregate.Window;
        var published = aggregate.Published;
        return new PublicationStatus(aggregate.Invitation.Id, aggregate.Invitation.PublicCode,
            aggregate.Invitation.State.ToString(), state.ToString(), now,
            window?.TimeZoneId ?? InitialPublicationContract.DefaultTimeZoneId, Revisions(aggregate),
            window is null ? null : new(window.Id, window.GrantId, window.StartsAt, window.EndsAt, window.TimeZoneId, window.Revision),
            published is null ? null : new(published.Revision, published.SourceWorkingRevision, published.TemplateKey, published.RendererVersion),
            published is not null && (aggregate.Working.Revision != published.SourceWorkingRevision ||
                aggregate.Invitation.TemplateKey != published.TemplateKey || aggregate.Invitation.RendererVersion != published.RendererVersion),
            AllowedActions(aggregate, state, now), new(preflight.TemplateAvailable, preflight.IsPremiumTemplate,
                preflight.MissingRequiredFields, preflight.MissingRecommendedFields),
            choices.Select(choice => new PublicationGrantChoice(choice.GrantId, choice.Label, choice.Kind,
                choice.MaxPublishDays, choice.MaxActiveInvitations, choice.PremiumTemplatesEnabled)).ToArray(),
            entitlements is null ? null : new(entitlements.MaxPublishDays, entitlements.MaxActiveInvitations, entitlements.PremiumTemplatesEnabled),
            await retentionSettings.ReadDaysAsync(cancellationToken));
    }

    private Task<PublicationPreflightResult> WorkingPreflightAsync(Aggregate aggregate, CancellationToken token) =>
        aggregate.Invitation.TemplateKey is null || aggregate.Invitation.RendererVersion is null
            ? Task.FromResult(new PublicationPreflightResult(false, false, [], []))
            : preflightValidator.ValidateAsync(aggregate.Invitation.TemplateKey, aggregate.Invitation.RendererVersion.Value,
                aggregate.Working.ContentSchemaVersion, aggregate.Working.Content, token);

    private async Task<IReadOnlyCollection<Guid>> StartedGrantIdsAsync(Guid accountId, DateTimeOffset now,
        CancellationToken cancellationToken) => await (
        from window in dbContext.PublicationWindows.AsNoTracking()
        join invitation in dbContext.Invitations.AsNoTracking() on window.InvitationId equals invitation.Id
        where invitation.AccountId == accountId && window.StartsAt <= now && window.IsCurrent
        select window.GrantId).Distinct().ToListAsync(cancellationToken);

    private WindowResolution ResolveWindow(PublicationWindowRequest request, DateTimeOffset now)
    {
        if (request.Mode is not "Immediate" and not "Scheduled")
            return new(false, default, default, Invalid("mode", "Publication mode must be Immediate or Scheduled."));
        if (!zoneValidator.IsValid(request.TimeZoneId))
            return new(false, default, default, Invalid("timeZoneId", "A valid IANA time-zone identifier is required."));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId.Trim());
        if (!TryResolveLocal(request.EndsAtLocal, zone, out var end, out var endError))
            return new(false, default, default, Invalid("endsAtLocal", endError));
        var start = now;
        var scheduled = request.Mode == "Scheduled";
        if (scheduled)
        {
            if (!TryResolveLocal(request.StartsAtLocal, zone, out start, out var startError))
                return new(true, default, default, Invalid("startsAtLocal", startError));
            if (start <= now) return new(true, default, default, Invalid("startsAtLocal", "Scheduled start must be in the future."));
        }
        else if (request.StartsAtLocal is not null)
            return new(false, default, default, Invalid("startsAtLocal", "Immediate publication must not specify a start."));
        if (end <= start) return new(scheduled, default, default, Invalid("endsAtLocal", "Publication end must be after its start."));
        if (request.RequestedGrantId == Guid.Empty) return new(scheduled, default, default, Invalid("requestedGrantId", "Grant id must not be empty."));
        return new(scheduled, start, end, null);
    }

    private static bool TryResolveLocal(string? input, TimeZoneInfo zone, out DateTimeOffset instant, out string error)
    {
        instant = default;
        if (!DateTime.TryParseExact(input, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            error = "Use a local date/time without an offset (yyyy-MM-ddTHH:mm[:ss]).";
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) { error = "This local time does not exist in the selected time zone."; return false; }
        if (zone.IsAmbiguousTime(local)) { error = "This local time is ambiguous in the selected time zone."; return false; }
        instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        error = string.Empty;
        return true;
    }

    private static PublicationLifecycleResult? ValidateActionRequest(PublicationActionRequest request)
    {
        if (request is null || request.Expected is null || !Actions.Contains(request.Action, StringComparer.Ordinal))
            return Invalid("action", "A supported action and expected revisions are required.");
        if (request.Expected.InvitationRevision < 0 || request.Expected.WorkingContentRevision < 0 ||
            request.Expected.PublishedContentRevision < 0 || request.Expected.WindowRevision < 0 ||
            (request.Expected.WindowId is null) != (request.Expected.WindowRevision is null) || request.Expected.WindowId == Guid.Empty)
            return Invalid("expected", "Expected revisions must be non-negative; window id and revision must be supplied together.");
        if ((request.Action is "publish" or "reschedule" or "reactivate") != (request.Publication is not null))
            return Invalid("publication", "Publication dates are required only for publish, reschedule and reactivate.");
        if (request.Action == "reschedule" && request.Publication!.Mode != "Scheduled")
            return Invalid("mode", "Reschedule requires Scheduled mode.");
        if (request.PublishWorkingContent && request.Action is not "resume" and not "republish")
            return Invalid("publishWorkingContent", "This option is supported only for resume or republish.");
        return null;
    }

    private static PublicationRevisions Revisions(Aggregate aggregate) => new(aggregate.Invitation.Revision,
        aggregate.Working.Revision, aggregate.Published?.Revision, aggregate.Window?.Id, aggregate.Window?.Revision);
    private static InvitationStoredState EffectiveState(Aggregate aggregate, DateTimeOffset now) =>
        InvitationEffectiveStateEvaluator.Evaluate(aggregate.Invitation.State, aggregate.Window, now);
    private static string[] AllowedActions(Aggregate aggregate, InvitationStoredState state, DateTimeOffset now) => state switch
    {
        InvitationStoredState.Draft when aggregate.Window is not null && aggregate.Window.EndsAt > now => ["republish"],
        InvitationStoredState.Draft => ["publish"],
        InvitationStoredState.Scheduled => ["update", "cancelSchedule", "reschedule", "publishNow"],
        InvitationStoredState.Active => ["update", "pause"],
        InvitationStoredState.Paused => ["update", "resume"],
        InvitationStoredState.Expired => ["reactivate"],
        _ => []
    };
    private static PublicationLifecycleResult Invalid(string field, string error) =>
        new("InvalidRequest", Errors: new Dictionary<string, string[]> { [field] = [error] });
    private static void Abort(string code) => throw new LifecycleAbort(new(code));
    private static void Abort(PublicationLifecycleResult result) => throw new LifecycleAbort(result);
    private sealed class LifecycleAbort(PublicationLifecycleResult result) : Exception("Publication lifecycle transaction aborted.")
    {
        public PublicationLifecycleResult Result { get; } = result;
    }
    private sealed record Aggregate(Invitation Invitation, WorkingContent Working,
        PublishedContent? Published, PublicationWindow? Window);
    private sealed record WindowResolution(bool Scheduled, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
        PublicationLifecycleResult? Error);
}
