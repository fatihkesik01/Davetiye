using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class CreatorMediaIntentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Draft")]
    [InlineData("Paused")]
    [InlineData("Expired")]
    [InlineData("Deleted")]
    public async Task Invitation_owned_lifecycle_gate_denies_non_issuable_states(string state)
    {
        var h = new Harness();
        h.Invitations.Access = h.Invitations.Access with { CanIssueCreatorMediaIntent = false, TemplateKey = state };

        var outcome = await h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.IneligibleInvitation, outcome.Failure);
        Assert.Empty(h.Uploads.Requests);
        Assert.Empty(h.Store.Saved);
    }

    [Fact]
    public async Task Draft_without_an_assigned_current_grant_is_denied()
    {
        var h = new Harness();
        h.Invitations.Access = h.Invitations.Access with { CanIssueCreatorMediaIntent = false, CurrentGrantId = null };

        var outcome = await h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.IneligibleInvitation, outcome.Failure);
        Assert.Empty(h.Uploads.Requests);
    }

    [Fact]
    public async Task Missing_or_revoked_grant_is_denied()
    {
        var h = new Harness();
        h.Entitlements.Result = EntitlementResolutionResult.Denied(EntitlementResolutionDenial.GrantNotEffective);

        var outcome = await h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.InvalidGrant, outcome.Failure);
        Assert.Empty(h.Uploads.Requests);
    }

    [Fact]
    public async Task Presentation_requires_the_template_supported_module()
    {
        var h = new Harness();
        h.Templates.SupportedModules = ["hero"];

        var outcome = await h.Service.CreateAsync(h.Command(role: MediaPresentationRole.Gallery), "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.UnsupportedModule, outcome.Failure);
        Assert.Empty(h.Uploads.Requests);
    }

    [Fact]
    public async Task Pending_and_ready_creator_media_usage_cannot_exceed_entitlement()
    {
        var h = new Harness();
        h.Store.Usage = (Images: 1L, Videos: 0L);
        h.Entitlements.Result = EntitlementResolutionResult.Granted(h.Entitlements.Entitlements with { MaxImages = 1 });

        var outcome = await h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.QuotaExceeded, outcome.Failure);
        Assert.Empty(h.Uploads.Requests);
        Assert.Empty(h.Store.Saved);
    }

    [Fact]
    public async Task Expired_open_reservation_is_closed_before_quota_is_read()
    {
        var h = new Harness();
        h.Store.Usage = (Images: 1L, Videos: 0L);
        h.Store.ExpiredReservationsToClose = 1;
        h.Entitlements.Result = EntitlementResolutionResult.Granted(h.Entitlements.Entitlements with { MaxImages = 1 });

        var outcome = await h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None);

        Assert.NotNull(outcome.Result);
        Assert.Equal(1, h.Store.ExpirationCount);
        Assert.True(h.Store.ExpirationHappenedBeforeUsageRead);
        Assert.Single(h.Store.Saved);
    }

    [Fact]
    public async Task Idempotent_replay_rechecks_eligibility_and_reuses_the_same_asset_capability_ceiling_and_expiry()
    {
        var h = new Harness();
        var command = h.Command();
        var initial = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);
        Assert.NotNull(initial.Result);
        var originalRequest = Assert.Single(h.Uploads.Requests);
        Assert.Equal(new[] { "save", "commit", "capability" }, h.Events);
        Assert.Equal(command.DeclaredByteLength, originalRequest.MaximumBytes);

        h.Invitations.Access = h.Invitations.Access with { CanIssueCreatorMediaIntent = false };
        var deniedReplay = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);
        Assert.Equal(CreatorMediaIntentFailure.IneligibleInvitation, deniedReplay.Failure);
        Assert.Single(h.Uploads.Requests);

        h.Invitations.Access = h.Invitations.Access with { CanIssueCreatorMediaIntent = true };
        var replay = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);
        Assert.NotNull(replay.Result);
        Assert.True(replay.Result.Replayed);
        Assert.Equal(initial.Result.AssetId, replay.Result.AssetId);
        Assert.Equal(initial.Result.ExpiresAt, replay.Result.ExpiresAt);
        Assert.Equal(initial.Capability, replay.Capability);
        Assert.Equal(2, h.Uploads.Requests.Count);
        var replayRequest = h.Uploads.Requests[1];
        Assert.Equal(originalRequest.AssetId, replayRequest.AssetId);
        Assert.Equal(originalRequest.MaximumBytes, replayRequest.MaximumBytes);
        Assert.Equal(originalRequest.ExpiresAt, replayRequest.ExpiresAt);
        Assert.Equal(new[] { "save", "commit", "capability", "commit", "commit", "capability" }, h.Events);
        Assert.Single(h.Store.Saved);
        Assert.Equal(1, h.Store.Usage.Images);
    }

    [Fact]
    public async Task Video_upload_capability_receives_the_effective_plan_duration_limit()
    {
        var h = new Harness();

        var outcome = await h.Service.CreateAsync(h.Command(kind: MediaKind.Video), "203.0.113.10", CancellationToken.None);

        Assert.NotNull(outcome.Capability);
        Assert.Equal(900, Assert.Single(h.Uploads.Requests).MaximumDurationSeconds);
    }

    [Fact]
    public async Task Expired_replay_is_closed_and_cannot_reissue_capability()
    {
        var h = new Harness();
        var command = h.Command();
        var initial = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);
        Assert.NotNull(initial.Result);
        h.Clock.UtcNowValue = initial.Result.ExpiresAt;

        var replay = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.IdempotencyConflict, replay.Failure);
        Assert.Single(h.Uploads.Requests);
        Assert.Equal(MediaAssetState.Rejected, h.Store.Saved.Single().Asset.State);
        Assert.NotNull(h.Store.Saved.Single().Intent.CancelledAt);
    }

    [Fact]
    public async Task Persistence_failure_never_calls_the_external_capability_issuer()
    {
        var h = new Harness();
        h.Store.ThrowOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None));

        Assert.Equal(new[] { "save" }, h.Events);
        Assert.Empty(h.Uploads.Requests);
    }

    [Fact]
    public async Task Transaction_commit_failure_never_calls_the_external_capability_issuer()
    {
        var h = new Harness();
        h.TransactionRunner.ThrowOnCommit = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None));

        Assert.Equal(new[] { "save", "commit" }, h.Events);
        Assert.Empty(h.Uploads.Requests);
    }

    [Fact]
    public async Task Existing_idempotency_key_with_different_request_fingerprint_conflicts()
    {
        var h = new Harness();
        var command = h.Command();
        var created = await h.Service.CreateAsync(command, "203.0.113.10", CancellationToken.None);
        Assert.NotNull(created.Result);

        var mismatch = await h.Service.CreateAsync(command with { DeclaredByteLength = command.DeclaredByteLength + 1 },
            "203.0.113.10", CancellationToken.None);

        Assert.Equal(CreatorMediaIntentFailure.IdempotencyConflict, mismatch.Failure);
        Assert.Single(h.Uploads.Requests);
        Assert.Single(h.Store.Saved);
    }

    [Fact]
    public async Task Concurrent_requests_for_the_last_creator_image_slot_allow_only_one_reservation()
    {
        var h = new Harness();
        h.Entitlements.Result = EntitlementResolutionResult.Granted(h.Entitlements.Entitlements with { MaxImages = 1 });

        var outcomes = await Task.WhenAll(
            h.Service.CreateAsync(h.Command(), "203.0.113.10", CancellationToken.None),
            h.Service.CreateAsync(h.Command(idempotencyKey: Guid.NewGuid()), "203.0.113.11", CancellationToken.None));

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Contains(outcomes, outcome => outcome.Failure == CreatorMediaIntentFailure.QuotaExceeded);
        Assert.Single(h.Store.Saved);
    }

    private sealed class Harness
    {
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid InvitationId { get; } = Guid.NewGuid();
        public List<string> Events { get; } = [];
        public FakeStore Store { get; }
        public FakeAccountValidator AccountValidator { get; } = new();
        public FakeInvitationReader Invitations { get; } = new();
        public FakeTemplateReader Templates { get; } = new();
        public FakeEntitlementResolver Entitlements { get; } = new();
        public FakeUploads Uploads { get; }
        public FakeClock Clock { get; } = new();
        public SerialTransactionRunner TransactionRunner { get; }
        public CreatorMediaIntentService Service { get; }

        public Harness()
        {
            Store = new FakeStore(Events);
            Uploads = new FakeUploads(Now, Events);
            var grantId = Guid.NewGuid();
            Invitations.Access = new CreatorMediaInvitationAccessSnapshot(true, grantId, "wedding-classic");
            Entitlements.Entitlements = new EffectiveEntitlementSnapshot(AccountId, grantId, Guid.NewGuid(), "one-time",
                PlanBillingKind.OneTime, GrantSource.IndividualPurchase, InvitationId, Now.AddMinutes(-5), Now.AddMinutes(-1),
                MaxPublishDays: 30, MaxActiveInvitations: 1, MaxImages: 3, MaxVideos: 1, MaxImageSizeMb: 5,
                MaxVideoSizeMb: 100, MaxVideoDurationSeconds: 900, MaxRsvpResponses: 500,
                MemoriesEnabled: false, GiftRegistryEnabled: false, PremiumTemplatesEnabled: false);
            Entitlements.Result = EntitlementResolutionResult.Granted(Entitlements.Entitlements);
            TransactionRunner = new SerialTransactionRunner(Events);
            Service = new CreatorMediaIntentService(Store, AccountValidator, Invitations, Templates, Entitlements,
                TransactionRunner, new AllowRateLimiter(), Uploads, Uploads, Clock);
        }

        public CreateCreatorMediaIntentCommand Command(Guid? idempotencyKey = null, MediaPresentationRole role = MediaPresentationRole.Cover, MediaKind kind = MediaKind.Image) =>
            new(AccountId, InvitationId, idempotencyKey ?? Guid.NewGuid(), kind, role, 4096);
    }

    private sealed class FakeStore(List<string> events) : ICreatorMediaIntentStore
    {
        public (long Images, long Videos) Usage { get; set; }
        public int ExpiredReservationsToClose { get; set; }
        public bool ThrowOnSave { get; set; }
        public int ExpirationCount { get; private set; }
        public bool UsageRead { get; private set; }
        public bool ExpirationHappenedBeforeUsageRead { get; private set; }
        public List<(MediaAsset Asset, PendingUpload Intent)> Saved { get; } = [];
        private readonly Dictionary<Guid, CreatorMediaReplaySnapshot> replays = [];

        public Task<CreatorMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken) =>
            Task.FromResult(replays.TryGetValue(key, out var replay) ? replay : null);

        public Task ExpireOpenReservationsAsync(Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ExpirationCount++;
            foreach (var key in replays.Keys.ToArray())
            {
                var replay = replays[key];
                if (replay.AssetState == MediaAssetState.PendingUpload && replay.CancelledAt is null &&
                    replay.ConsumedAt is null && replay.ExpiresAt <= now)
                {
                    replays[key] = replay with { AssetState = MediaAssetState.Rejected, CancelledAt = now };
                }
            }

            foreach (var reservation in Saved.Where(item => item.Intent.ConsumedAt is null &&
                         item.Intent.CancelledAt is null && item.Intent.ExpiresAt <= now))
            {
                reservation.Intent.CancelExpired(now);
                reservation.Asset.Reject();
            }

            if (ExpiredReservationsToClose > 0)
            {
                Usage = (Math.Max(0, Usage.Images - ExpiredReservationsToClose), Usage.Videos);
            }
            return Task.CompletedTask;
        }

        public Task<(long Images, long Videos)> GetCreatorUsageAsync(Guid invitationId, CancellationToken cancellationToken)
        {
            ExpirationHappenedBeforeUsageRead = ExpirationCount > 0;
            UsageRead = true;
            return Task.FromResult(Usage);
        }

        public Task SaveIntentAsync(MediaAsset asset, PendingUpload intent, CancellationToken cancellationToken)
        {
            events.Add("save");
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Synthetic persistence failure.");
            }

            Saved.Add((asset, intent));
            Usage = asset.Kind == MediaKind.Image ? (Usage.Images + 1, Usage.Videos) : (Usage.Images, Usage.Videos + 1);
            replays[intent.IdempotencyKey] = new CreatorMediaReplaySnapshot(asset.InvitationId, intent.Id, asset.Id, asset.Kind, asset.State,
                intent.RequestedPresentationRole, intent.DeclaredByteLength, intent.RequestFingerprint,
                intent.ExpiresAt, intent.ConsumedAt, intent.CancelledAt);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountValidator : IAccountReferenceValidator
    {
        public AccountReferenceStatus Status { get; set; } = AccountReferenceStatus.Verified;
        public Task<AccountReferenceStatus> GetStatusAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Status);
    }

    private sealed class FakeInvitationReader : ICreatorMediaInvitationAccessReader
    {
        public CreatorMediaInvitationAccessSnapshot Access { get; set; } = new(true, Guid.NewGuid(), "wedding-classic");
        public Task<CreatorMediaInvitationAccessSnapshot?> LoadAsync(Guid accountId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<CreatorMediaInvitationAccessSnapshot?>(Access);
    }

    private sealed class FakeTemplateReader : ITemplateSelectionResolver
    {
        public IReadOnlyCollection<string>? SupportedModules { get; set; } = ["hero", "gallery"];
        public Task<TemplateSelection?> ResolveActiveAsync(string templateKey, CancellationToken cancellationToken) =>
            Task.FromResult<TemplateSelection?>(new TemplateSelection(templateKey, 1, false, SupportedModules?.ToArray() ?? []));
    }

    private sealed class FakeEntitlementResolver : IEffectiveEntitlementResolver
    {
        public EffectiveEntitlementSnapshot Entitlements { get; set; } = null!;
        public EntitlementResolutionResult Result { get; set; } = EntitlementResolutionResult.Denied(EntitlementResolutionDenial.GrantNotFoundOrNotOwned);
        public EntitlementResolutionContext? Context { get; private set; }
        public Task<EntitlementResolutionResult> ResolveAsync(EntitlementResolutionContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeUploads(DateTimeOffset now, List<string> events) : IMediaUploadGateway, IMediaImageNormalizationPipeline
    {
        public List<MediaUploadRequest> Requests { get; } = [];
        private readonly Dictionary<Guid, MediaUploadCapability> capabilities = [];

        public Task<MediaUploadCapability> CreateVideoCapabilityAsync(MediaUploadRequest request, CancellationToken cancellationToken) => Create(request);
        public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(MediaUploadRequest request, CancellationToken cancellationToken) => Create(request);
        public Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken) => Task.FromResult<MediaProviderVideoInspection?>(null);
        public Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken) => Task.FromResult<NormalizedImageVerificationEvidence?>(null);
        public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken) => Task.CompletedTask;

        private Task<MediaUploadCapability> Create(MediaUploadRequest request)
        {
            events.Add("capability");
            Requests.Add(request);
            if (capabilities.TryGetValue(request.AssetId, out var existing))
            {
                return Task.FromResult(existing);
            }

            var capability = new MediaUploadCapability(new Uri($"https://upload.example.test/{request.AssetId:N}"),
                request.ExpiresAt < now.AddMinutes(5) ? request.ExpiresAt : now.AddMinutes(5),
                request.Kind == MediaKind.Image ? new Dictionary<string, string> { ["X-Media-Capability"] = "signed-capability" } : null);
            capabilities.Add(request.AssetId, capability);
            return Task.FromResult(capability);
        }
    }

    private sealed class SerialTransactionRunner(List<string> events) : IOutermostAccountQuotaTransactionRunner
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        public bool ThrowOnCommit { get; set; }

        public async Task<TResult> ExecuteAndCommitAsync<TResult>(Guid accountId, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                events.Add("commit");
                if (ThrowOnCommit)
                {
                    throw new InvalidOperationException("Synthetic transaction commit failure.");
                }
                return result;
            }
            finally { gate.Release(); }
        }
    }

    private sealed class AllowRateLimiter : ICreatorMediaIntentRateLimiter
    {
        public bool TryAcquire(Guid accountId, string clientIpAddress) => true;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNowValue { get; set; } = Now;
        public DateTimeOffset UtcNow => UtcNowValue;
    }
}
