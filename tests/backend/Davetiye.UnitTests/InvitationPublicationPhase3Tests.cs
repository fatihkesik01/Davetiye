using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class InvitationPublicationPhase3Tests
{
    private const string PublicCode =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now =
        new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void Invitation_rejects_public_codes_outside_the_256_bit_lowercase_hex_contract(
        string publicCode)
    {
        Assert.Throws<ArgumentException>(() => Invitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), publicCode, Now));
    }

    [Fact]
    public void Invitation_and_published_snapshot_reject_non_utc_authoritative_timestamps()
    {
        var nonUtc = Now.ToOffset(TimeSpan.FromHours(3));
        Assert.Throws<ArgumentException>(() => Invitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), PublicCode, nonUtc));

        var invitation = CreateDraft();
        var working = CreateWorking(invitation.Id);
        Assert.Throws<ArgumentException>(() => PublishedContent.Create(
            Guid.NewGuid(), invitation.Id, "classic", 1, 1, working.Content,
            working.Revision, nonUtc));
    }

    [Fact]
    public void Published_snapshot_keeps_an_independent_template_and_working_payload_copy()
    {
        var invitation = CreateDraft();
        var working = CreateWorking(invitation.Id);
        var published = PublishedContent.Create(
            Guid.NewGuid(), invitation.Id, invitation.TemplateKey!, invitation.RendererVersion!.Value,
            working.ContentSchemaVersion, working.Content, working.Revision, Now);

        invitation.PinTemplate("modern", 2);
        working.ReplaceContent("""{"headline":"Changed"}""", 1, Now.AddMinutes(1));

        Assert.Equal("classic", published.TemplateKey);
        Assert.Equal(1, published.RendererVersion);
        Assert.Equal("""{"headline":"Original"}""", published.Content);
        Assert.Equal(0, published.SourceWorkingRevision);
    }

    [Fact]
    public void Publication_window_requires_utc_and_keeps_its_permanent_grant_link()
    {
        var grantId = Guid.NewGuid();
        var window = PublicationWindow.Create(
            Guid.NewGuid(), Guid.NewGuid(), grantId, Now, Now.AddDays(1),
            InitialPublicationContract.DefaultTimeZoneId, Now);

        Assert.Equal(grantId, window.GrantId);
        Assert.True(window.IsCurrent);
        Assert.Throws<ArgumentException>(() => PublicationWindow.Create(
            Guid.NewGuid(), Guid.NewGuid(), grantId,
            Now.ToOffset(TimeSpan.FromHours(3)),
            Now.AddDays(1),
            InitialPublicationContract.DefaultTimeZoneId,
            Now));
    }

    [Fact]
    public void Effective_state_uses_half_open_window_boundaries_even_if_worker_is_delayed()
    {
        var window = PublicationWindow.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(1), Now.AddHours(2), "Europe/Istanbul", Now);

        Assert.Equal(
            InvitationStoredState.Scheduled,
            InvitationEffectiveStateEvaluator.Evaluate(
                InvitationStoredState.Scheduled, window, Now));
        Assert.Equal(
            InvitationStoredState.Active,
            InvitationEffectiveStateEvaluator.Evaluate(
                InvitationStoredState.Scheduled, window, Now.AddHours(1)));
        Assert.Equal(
            InvitationStoredState.Expired,
            InvitationEffectiveStateEvaluator.Evaluate(
                InvitationStoredState.Scheduled, window, Now.AddHours(2)));
        Assert.Equal(
            InvitationStoredState.Paused,
            InvitationEffectiveStateEvaluator.Evaluate(
                InvitationStoredState.Paused, window, Now.AddHours(1)));
    }

    [Fact]
    public async Task Immediate_publish_atomically_creates_snapshot_window_and_active_state()
    {
        var harness = CreateHarness();
        var result = await harness.Service.PublishAsync(
            harness.AccountId,
            harness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Immediate, startsAt: null, endsAt: Now.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.Succeeded, result.Outcome);
        Assert.Equal(InvitationStoredState.Active, harness.Draft.Invitation.State);
        Assert.Equal(PublicCode, result.PublicCode);
        Assert.NotNull(harness.Store.SavedPublishedContent);
        Assert.Equal("classic", harness.Store.SavedPublishedContent!.TemplateKey);
        Assert.Equal(
            harness.Allocator.Result.Entitlements!.GrantId,
            harness.Store.SavedPublicationWindow!.GrantId);
        Assert.Equal(Now, harness.Store.SavedPublicationWindow.StartsAt);
        Assert.Equal(1, harness.TransactionRunner.CommitCount);
        Assert.Equal(0, harness.TransactionRunner.RollbackCount);
    }

    [Fact]
    public async Task Schedule_freezes_snapshot_now_but_uses_future_window_start()
    {
        var harness = CreateHarness();
        var startsAt = Now.AddDays(2);
        var result = await harness.Service.PublishAsync(
            harness.AccountId,
            harness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Scheduled, startsAt, startsAt.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.Succeeded, result.Outcome);
        Assert.Equal(InvitationStoredState.Scheduled, harness.Draft.Invitation.State);
        Assert.Equal(startsAt, harness.Store.SavedPublicationWindow!.StartsAt);
        Assert.Equal(Now, harness.Store.SavedPublishedContent!.PublishedAt);
        Assert.Equal(Now, harness.Allocator.LastRequest!.OccurredAtUtc);
        Assert.Equal(PublicationEntitlementAction.Schedule, harness.Allocator.LastRequest.Action);
    }

    [Fact]
    public async Task Foreign_invitation_and_banned_account_do_not_reach_grant_allocator()
    {
        var missingHarness = CreateHarness();
        missingHarness.Store.Draft = null;
        var missing = await missingHarness.Service.PublishAsync(
            missingHarness.AccountId,
            Guid.NewGuid(),
            Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)),
            CancellationToken.None);

        var bannedHarness = CreateHarness(AccountReferenceStatus.Banned);
        var banned = await bannedHarness.Service.PublishAsync(
            bannedHarness.AccountId,
            bannedHarness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.NotFound, missing.Outcome);
        Assert.Equal(InitialPublicationOutcome.AccountInactive, banned.Outcome);
        Assert.Equal(0, missingHarness.Allocator.CallCount);
        Assert.Equal(0, bannedHarness.Allocator.CallCount);
    }

    [Fact]
    public async Task Recommended_fields_require_explicit_confirmation_before_grant_allocation()
    {
        var harness = CreateHarness();
        harness.Preflight.Result = new PublicationPreflightResult(
            true, false, [], ["venue.address"]);

        var result = await harness.Service.PublishAsync(
            harness.AccountId,
            harness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.RecommendedFieldsRequireConfirmation, result.Outcome);
        Assert.Equal(0, harness.Allocator.CallCount);
        Assert.Null(harness.Store.SavedPublishedContent);
    }

    [Fact]
    public async Task Premium_or_quota_denial_after_allocation_rolls_back_the_transaction()
    {
        var premiumHarness = CreateHarness(premiumTemplatesEnabled: false);
        premiumHarness.Preflight.Result = new PublicationPreflightResult(true, true, [], []);
        var premium = await premiumHarness.Service.PublishAsync(
            premiumHarness.AccountId,
            premiumHarness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)),
            CancellationToken.None);

        var quotaHarness = CreateHarness(maxActiveInvitations: 1);
        quotaHarness.Store.Slots =
        [
            new PublicationQuotaSlot(
                Guid.NewGuid(),
                Now, Now.AddDays(2))
        ];
        var quota = await quotaHarness.Service.PublishAsync(
            quotaHarness.AccountId,
            quotaHarness.Draft.Invitation.Id,
            Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.PremiumTemplateNotAllowed, premium.Outcome);
        Assert.Equal(InitialPublicationOutcome.ActiveInvitationQuotaExceeded, quota.Outcome);
        Assert.Equal(1, premiumHarness.TransactionRunner.RollbackCount);
        Assert.Equal(1, quotaHarness.TransactionRunner.RollbackCount);
        Assert.Equal(0, premiumHarness.TransactionRunner.CommitCount);
        Assert.Equal(0, quotaHarness.TransactionRunner.CommitCount);
    }

    [Fact]
    public async Task Stale_revisions_are_rejected_before_entitlement_mutation()
    {
        var harness = CreateHarness();
        var request = Request(InitialPublicationMode.Immediate, null, Now.AddDays(1)) with
        {
            ExpectedWorkingContentRevision = 99
        };

        var result = await harness.Service.PublishAsync(
            harness.AccountId,
            harness.Draft.Invitation.Id,
            request,
            CancellationToken.None);

        Assert.Equal(InitialPublicationOutcome.Conflict, result.Outcome);
        Assert.Equal(0, harness.Allocator.CallCount);
    }

    private static InitialPublicationRequest Request(
        InitialPublicationMode mode,
        DateTimeOffset? startsAt,
        DateTimeOffset endsAt) =>
        new(
            RequestedGrantId: null,
            mode,
            startsAt,
            endsAt,
            InitialPublicationContract.DefaultTimeZoneId,
            ExpectedInvitationRevision: 1,
            ExpectedWorkingContentRevision: 0,
            ProceedWithRecommendedWarnings: false);

    private static Harness CreateHarness(
        AccountReferenceStatus accountStatus = AccountReferenceStatus.Verified,
        bool premiumTemplatesEnabled = true,
        long maxActiveInvitations = 1)
    {
        var accountId = Guid.NewGuid();
        var invitation = CreateDraft(accountId);
        var draft = new InitialPublicationDraft(
            invitation,
            CreateWorking(invitation.Id),
            HasPublishedContent: false,
            HasCurrentPublicationWindow: false);
        var store = new StubStore { Draft = draft };
        var preflight = new StubPreflight();
        var allocator = new StubAllocator
        {
            Result = PublicationGrantAllocationResult.Allocated(
                Entitlements(
                    accountId,
                    invitation.Id,
                    premiumTemplatesEnabled,
                    maxActiveInvitations))
        };
        var runner = new StubTransactionRunner();
        var service = new InitialPublicationService(
            new StubAccountValidator(accountStatus),
            store,
            preflight,
            new StubTimeZoneValidator(),
            allocator,
            runner,
            new StubClock(Now));

        return new Harness(accountId, draft, store, preflight, allocator, runner, service);
    }

    private static Invitation CreateDraft(Guid? accountId = null)
    {
        var invitation = Invitation.Create(
            Guid.NewGuid(), accountId ?? Guid.NewGuid(), PublicCode, Now);
        invitation.PinTemplate("classic", 1);
        return invitation;
    }

    private static WorkingContent CreateWorking(Guid invitationId) =>
        WorkingContent.Create(
            Guid.NewGuid(), invitationId, 1, """{"headline":"Original"}""", Now);

    private static EffectiveEntitlementSnapshot Entitlements(
        Guid accountId,
        Guid invitationId,
        bool premiumTemplatesEnabled,
        long maxActiveInvitations) =>
        new(
            accountId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "premium",
            PlanBillingKind.OneTime,
            GrantSource.Free,
            AssignedInvitationId: invitationId,
            ReservedAt: Now,
            ConsumedAt: Now,
            MaxPublishDays: 30,
            maxActiveInvitations,
            MaxImages: 100,
            MaxVideos: 5,
            MaxImageSizeMb: 10,
            MaxVideoSizeMb: 500,
            MaxVideoDurationSeconds: 600,
            MaxRsvpResponses: 1000,
            MemoriesEnabled: true,
            GiftRegistryEnabled: true,
            premiumTemplatesEnabled);

    private sealed record Harness(
        Guid AccountId,
        InitialPublicationDraft Draft,
        StubStore Store,
        StubPreflight Preflight,
        StubAllocator Allocator,
        StubTransactionRunner TransactionRunner,
        InitialPublicationService Service);

    private sealed class StubAccountValidator(AccountReferenceStatus status)
        : IAccountReferenceValidator
    {
        public Task<AccountReferenceStatus> GetStatusAsync(
            Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private sealed class StubTimeZoneValidator : IIanaTimeZoneValidator
    {
        public bool IsValid(string timeZoneId) => timeZoneId == "Europe/Istanbul";
    }

    private sealed class StubPreflight : IInitialPublicationPreflightValidator
    {
        public PublicationPreflightResult Result { get; set; } = new(true, false, [], []);

        public Task<PublicationPreflightResult> ValidateAsync(
            string templateKey,
            int rendererVersion,
            int contentSchemaVersion,
            string content,
            CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private sealed class StubAllocator : IPublicationGrantAllocator
    {
        public PublicationGrantAllocationResult Result { get; set; } =
            PublicationGrantAllocationResult.Denied(PublicationGrantAllocationDenial.GrantUnavailable);

        public int CallCount { get; private set; }

        public PublicationGrantAllocationRequest? LastRequest { get; private set; }

        public Task<PublicationGrantAllocationResult> AllocateAsync(
            PublicationGrantAllocationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (Result.Entitlements is not null &&
                request.Action == PublicationEntitlementAction.Schedule)
            {
                return Task.FromResult(Result with
                {
                    Entitlements = Result.Entitlements with { ConsumedAt = null }
                });
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class StubStore : IInitialPublicationStore
    {
        public InitialPublicationDraft? Draft { get; set; }

        public IReadOnlyList<PublicationQuotaSlot> Slots { get; set; } = [];

        public PublishedContent? SavedPublishedContent { get; private set; }

        public PublicationWindow? SavedPublicationWindow { get; private set; }

        public Task<InitialPublicationDraft?> LoadOwnedDraftAsync(
            Guid accountId,
            Guid invitationId,
            CancellationToken cancellationToken) => Task.FromResult(Draft);

        public Task<IReadOnlyList<PublicationQuotaSlot>> ListAccountPublicationSlotsAsync(
            Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(Slots);

        public Task<InitialPublicationSaveResult> SaveAsync(
            Invitation invitation,
            PublishedContent publishedContent,
            PublicationWindow publicationWindow,
            CancellationToken cancellationToken)
        {
            SavedPublishedContent = publishedContent;
            SavedPublicationWindow = publicationWindow;
            return Task.FromResult(new InitialPublicationSaveResult(InitialPublicationSaveOutcome.Saved));
        }
    }

    private sealed class StubTransactionRunner : IAccountQuotaTransactionRunner
    {
        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public async Task<TResult> ExecuteAsync<TResult>(
            Guid accountId,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await operation(cancellationToken);
                CommitCount++;
                return result;
            }
            catch
            {
                RollbackCount++;
                throw;
            }
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
