using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Invitations;

/// <summary>
/// Initial Draft publication orchestration. Every authoritative read and mutation after entering
/// the callback is protected by the account-scoped transaction/advisory lock.
/// </summary>
public sealed class InitialPublicationService(
    IAccountReferenceValidator accountReferenceValidator,
    IInitialPublicationStore store,
    IInitialPublicationPreflightValidator preflightValidator,
    IIanaTimeZoneValidator timeZoneValidator,
    IPublicationGrantAllocator grantAllocator,
    IAccountQuotaTransactionRunner transactionRunner,
    IClock clock) : IInitialPublicationService
{
    public async Task<InitialPublicationResult> PublishAsync(
        Guid accountId,
        Guid invitationId,
        InitialPublicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIds(accountId, invitationId);

        var now = clock.UtcNow.ToUniversalTime();
        var requestError = ValidateRequest(request, now);
        if (requestError is not null)
        {
            return requestError;
        }

        try
        {
            return await transactionRunner.ExecuteAsync(
                accountId,
                token => PublishInsideTransactionAsync(
                    accountId,
                    invitationId,
                    request,
                    now,
                    token),
                cancellationToken);
        }
        catch (PublicationTransactionAbortException exception)
        {
            return exception.Result;
        }
    }

    private async Task<InitialPublicationResult> PublishInsideTransactionAsync(
        Guid accountId,
        Guid invitationId,
        InitialPublicationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var draft = await store.LoadOwnedDraftAsync(accountId, invitationId, cancellationToken);
        if (draft is null)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.NotFound);
        }

        // Both the account and row locks may have waited. Admission uses the instant after
        // obtaining the authoritative Working snapshot, never the request's earlier clock.
        now = clock.UtcNow.ToUniversalTime();
        var requestError = ValidateRequest(request, now);
        if (requestError is not null)
        {
            return requestError;
        }

        var accountStatus = await accountReferenceValidator.GetStatusAsync(accountId, cancellationToken);
        if (accountStatus != AccountReferenceStatus.Verified)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.AccountInactive);
        }

        if (draft.Invitation.State != InvitationStoredState.Draft ||
            draft.HasCurrentPublicationWindow)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.InvalidState);
        }

        if (draft.Invitation.Revision != request.ExpectedInvitationRevision ||
            draft.WorkingContent.Revision != request.ExpectedWorkingContentRevision)
        {
            return Conflict(draft);
        }

        if (draft.Invitation.TemplateKey is null || draft.Invitation.RendererVersion is null)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.TemplateUnavailable);
        }

        var preflight = await preflightValidator.ValidateAsync(
            draft.Invitation.TemplateKey,
            draft.Invitation.RendererVersion.Value,
            draft.WorkingContent.ContentSchemaVersion,
            draft.WorkingContent.Content,
            cancellationToken);

        if (!preflight.TemplateAvailable)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.TemplateUnavailable);
        }

        if (preflight.MissingRequiredFields.Count > 0)
        {
            return new InitialPublicationResult(
                InitialPublicationOutcome.RequiredFieldsMissing,
                MissingRequiredFields: preflight.MissingRequiredFields);
        }

        if (preflight.MissingRecommendedFields.Count > 0 &&
            !request.ProceedWithRecommendedWarnings)
        {
            return new InitialPublicationResult(
                InitialPublicationOutcome.RecommendedFieldsRequireConfirmation,
                MissingRecommendedFields: preflight.MissingRecommendedFields);
        }

        var scheduled = request.Mode == InitialPublicationMode.Scheduled;
        var startsAt = scheduled ? request.ScheduledStartsAtUtc!.Value : now;
        var entitlementAction = scheduled
            ? PublicationEntitlementAction.Schedule
            : PublicationEntitlementAction.PublishNow;

        var allocation = await grantAllocator.AllocateAsync(
            new PublicationGrantAllocationRequest(
                accountId,
                invitationId,
                request.RequestedGrantId,
                entitlementAction,
                now),
            cancellationToken);

        if (!allocation.IsAllocated)
        {
            return new InitialPublicationResult(InitialPublicationOutcome.GrantUnavailable);
        }

        var entitlements = allocation.Entitlements!;
        if (!PublicationAdmission.AllocationMatchesRequest(
                entitlements,
                request.RequestedGrantId,
                accountId,
                invitationId,
                scheduled))
        {
            Abort(new InitialPublicationResult(InitialPublicationOutcome.GrantUnavailable));
        }

        if (preflight.IsPremiumTemplate && !entitlements.PremiumTemplatesEnabled)
        {
            Abort(new InitialPublicationResult(InitialPublicationOutcome.PremiumTemplateNotAllowed));
        }

        var slots = await store.ListAccountPublicationSlotsAsync(accountId, cancellationToken);
        var quotaDecision = PublicationAdmission.EvaluateQuota(
            invitationId,
            startsAt,
            request.EndsAtUtc,
            entitlements.MaxPublishDays,
            entitlements.MaxActiveInvitations,
            slots);

        if (!quotaDecision.IsAllowed)
        {
            Abort(new InitialPublicationResult(
                quotaDecision.Denial == PublicationAdmissionDenial.PublishDurationExceeded
                    ? InitialPublicationOutcome.PublishDurationExceeded
                    : InitialPublicationOutcome.ActiveInvitationQuotaExceeded));
        }

        var publishedContent = PublishedContent.Create(
            Guid.NewGuid(),
            invitationId,
            draft.Invitation.TemplateKey,
            draft.Invitation.RendererVersion.Value,
            draft.WorkingContent.ContentSchemaVersion,
            draft.WorkingContent.Content,
            draft.WorkingContent.Revision,
            now,
            JsonSerializer.Serialize(draft.MediaPlacements ?? []));

        var publicationWindow = PublicationWindow.Create(
            Guid.NewGuid(),
            invitationId,
            entitlements.GrantId,
            startsAt,
            request.EndsAtUtc,
            request.TimeZoneId,
            now);

        draft.Invitation.BeginInitialPublication(scheduled);

        var saveOutcome = await store.SaveAsync(
            draft.Invitation,
            publishedContent,
            publicationWindow,
            cancellationToken);
        if (saveOutcome.Outcome == InitialPublicationSaveOutcome.Conflict)
        {
            Abort(new InitialPublicationResult(
                InitialPublicationOutcome.Conflict,
                CurrentInvitationRevision: saveOutcome.CurrentInvitationRevision,
                CurrentWorkingContentRevision: saveOutcome.CurrentWorkingContentRevision));
        }

        return new InitialPublicationResult(
            InitialPublicationOutcome.Succeeded,
            draft.Invitation.Id,
            draft.Invitation.PublicCode,
            draft.Invitation.State,
            publicationWindow.StartsAt,
            publicationWindow.EndsAt,
            MissingRecommendedFields: preflight.MissingRecommendedFields);
    }

    private InitialPublicationResult? ValidateRequest(
        InitialPublicationRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!Enum.IsDefined(request.Mode))
        {
            errors["mode"] = ["Publication mode is not supported."];
        }

        if (request.ExpectedInvitationRevision < 0)
        {
            errors["expectedInvitationRevision"] = ["Expected revision must not be negative."];
        }

        if (request.ExpectedWorkingContentRevision < 0)
        {
            errors["expectedWorkingContentRevision"] = ["Expected revision must not be negative."];
        }

        if (request.RequestedGrantId == Guid.Empty)
        {
            errors["requestedGrantId"] = ["Requested grant id must not be empty when supplied."];
        }

        if (request.EndsAtUtc.Offset != TimeSpan.Zero)
        {
            errors["endsAtUtc"] = ["Publication end must be expressed in UTC."];
        }

        if (string.IsNullOrWhiteSpace(request.TimeZoneId) ||
            request.TimeZoneId.Length > 100 ||
            !timeZoneValidator.IsValid(request.TimeZoneId))
        {
            errors["timeZoneId"] = ["A valid IANA time-zone id is required."];
        }

        if (request.Mode == InitialPublicationMode.Immediate)
        {
            if (request.ScheduledStartsAtUtc is not null)
            {
                errors["scheduledStartsAtUtc"] = ["Immediate publication must not specify a scheduled start."];
            }

            if (request.EndsAtUtc <= now)
            {
                errors["endsAtUtc"] = ["Publication end must be after its start."];
            }
        }
        else if (request.Mode == InitialPublicationMode.Scheduled)
        {
            if (request.ScheduledStartsAtUtc is null ||
                request.ScheduledStartsAtUtc.Value.Offset != TimeSpan.Zero)
            {
                errors["scheduledStartsAtUtc"] = ["Scheduled publication requires a UTC start."];
            }
            else if (request.ScheduledStartsAtUtc.Value <= now)
            {
                errors["scheduledStartsAtUtc"] = ["Scheduled publication start must be in the future."];
            }
            else if (request.EndsAtUtc <= request.ScheduledStartsAtUtc.Value)
            {
                errors["endsAtUtc"] = ["Publication end must be after its start."];
            }
        }

        return errors.Count == 0
            ? null
            : new InitialPublicationResult(InitialPublicationOutcome.InvalidRequest, Errors: errors);
    }

    private static InitialPublicationResult Conflict(InitialPublicationDraft draft) =>
        new(
            InitialPublicationOutcome.Conflict,
            CurrentInvitationRevision: draft.Invitation.Revision,
            CurrentWorkingContentRevision: draft.WorkingContent.Revision);

    private static void ValidateIds(Guid accountId, Guid invitationId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty.", nameof(invitationId));
        }
    }

    private static void Abort(InitialPublicationResult result) =>
        throw new PublicationTransactionAbortException(result);

    private sealed class PublicationTransactionAbortException(InitialPublicationResult result)
        : Exception("Initial publication transaction was intentionally aborted.")
    {
        public InitialPublicationResult Result { get; } = result;
    }
}
