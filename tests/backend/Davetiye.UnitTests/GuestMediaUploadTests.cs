using System.Net;
using Davetiye.Application.Modules.Media;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class GuestMediaUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData(EntitlementCatalog.MaxGuestImages, EntitlementCatalog.MaxImages)]
    [InlineData(EntitlementCatalog.MaxGuestVideos, EntitlementCatalog.MaxVideos)]
    [InlineData(EntitlementCatalog.MaxGuestImageSizeMb, EntitlementCatalog.MaxImageSizeMb)]
    [InlineData(EntitlementCatalog.MaxGuestVideoSizeMb, EntitlementCatalog.MaxVideoSizeMb)]
    [InlineData(EntitlementCatalog.MaxGuestVideoDurationSeconds, EntitlementCatalog.MaxVideoDurationSeconds)]
    public void Guest_entitlement_ceilings_never_exceed_the_matching_creator_ceiling(string guestKey, string creatorKey)
    {
        var guest = EntitlementCatalog.Require(guestKey);
        Assert.Equal(EntitlementValueType.Numeric, guest.ValueType);
        Assert.True(guest.HardCeiling <= EntitlementCatalog.Require(creatorKey).HardCeiling);
        Assert.NotNull(PlanEntitlement.Create(Guid.NewGuid(), Guid.NewGuid(), guestKey, guest.HardCeiling, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlement.Create(
            Guid.NewGuid(), Guid.NewGuid(), guestKey, guest.HardCeiling + 1, null));
    }

    [Theory]
    [InlineData(EntitlementCatalog.MaxGuestImages, 100)]
    [InlineData(EntitlementCatalog.MaxGuestVideos, 10)]
    [InlineData(EntitlementCatalog.MaxGuestImageSizeMb, 10)]
    [InlineData(EntitlementCatalog.MaxGuestVideoSizeMb, 100)]
    [InlineData(EntitlementCatalog.MaxGuestVideoDurationSeconds, 60)]
    public void Accepted_guest_starting_values_fit_inside_the_ceilings(string key, long accepted) =>
        Assert.True(accepted <= EntitlementCatalog.Require(key).HardCeiling);

    [Fact]
    public void Guest_asset_is_guest_scoped_and_starts_pending()
    {
        var asset = MediaAsset.CreateGuestAsset(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, Now);
        Assert.Equal(MediaQuotaScope.Guest, asset.QuotaScope);
        Assert.Equal(MediaAssetState.PendingUpload, asset.State);
    }

    [Fact]
    public async Task Oversize_or_overlong_requests_are_rejected_before_any_store_write_or_provider_call()
    {
        var provider = new Provider();
        var store = new Store();
        var service = Service(store, provider);

        foreach (var command in new[]
                 {
                     Command(GuestMediaKind.Image, 10 * Mb + 1, 0),
                     Command(GuestMediaKind.Video, 100 * Mb + 1, 30),
                     Command(GuestMediaKind.Video, 50 * Mb, 61),
                 })
        {
            var outcome = await service.ReserveAsync(command, default);
            Assert.Equal(GuestMediaReservationFailure.EntitlementLimit, outcome.Failure);
        }

        Assert.Equal(0, store.Saved);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Quota_is_checked_per_kind_and_the_owner_count_is_enforced()
    {
        var store = new Store { Images = 100, Videos = 0 };
        var service = Service(store, new Provider());

        Assert.Equal(GuestMediaReservationFailure.QuotaExceeded,
            (await service.ReserveAsync(Command(GuestMediaKind.Image, Mb, 0), default)).Failure);
        Assert.NotNull((await service.ReserveAsync(Command(GuestMediaKind.Video, Mb, 10), default)).Reservation);
        Assert.Equal(GuestMediaReservationFailure.OwnerLimit,
            (await service.ReserveAsync(Command(GuestMediaKind.Video, Mb, 10) with { OwnerItemCount = 3 }, default)).Failure);
    }

    [Fact]
    public async Task Provider_capability_is_guest_scoped_bound_to_the_reservation_and_unsafe_responses_are_dropped()
    {
        var provider = new Provider();
        var service = Service(new Store(), provider);
        var reservation = (await service.ReserveAsync(Command(GuestMediaKind.Image, 2 * Mb, 0), default)).Reservation!;

        var capability = await service.IssueCapabilityAsync(reservation, default);
        Assert.NotNull(capability);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(MediaQuotaScope.Guest, request.QuotaScope);
        Assert.Equal(reservation.AssetId, request.AssetId);
        Assert.Equal(10 * Mb, request.MaximumBytes);
        Assert.Equal(reservation.ExpiresAt, request.ExpiresAt);

        provider.Unsafe = true;
        Assert.Null(await service.IssueCapabilityAsync(reservation, default));
        provider.Unsafe = false;
        provider.Throw = true;
        Assert.Null(await service.IssueCapabilityAsync(reservation, default));
    }

    [Fact]
    public async Task Entitlement_without_memories_or_guest_size_makes_the_reservation_invalid_grant()
    {
        var service = Service(new Store(), new Provider(), memoriesEnabled: false);
        Assert.Equal(GuestMediaReservationFailure.InvalidGrant,
            (await service.ReserveAsync(Command(GuestMediaKind.Image, Mb, 0), default)).Failure);
        service = Service(new Store(), new Provider(), imageMb: 0);
        Assert.Equal(GuestMediaReservationFailure.InvalidGrant,
            (await service.ReserveAsync(Command(GuestMediaKind.Image, Mb, 0), default)).Failure);
    }

    private static GuestMediaReservationCommand Command(GuestMediaKind kind, long bytes, long duration) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, bytes, duration,
        Now.AddMinutes(10), OwnerItemCount: 0, OwnerItemLimit: 3);

    private static GuestMediaUploadService Service(Store store, Provider provider, bool memoriesEnabled = true, long imageMb = 10) =>
        new(store, new Resolver(memoriesEnabled, imageMb), provider, provider, new FixedClock());

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }

    private sealed class Resolver(bool memoriesEnabled, long imageMb) : IEffectiveEntitlementResolver
    {
        public Task<EntitlementResolutionResult> ResolveAsync(EntitlementResolutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(EntitlementResolutionResult.Granted(new EffectiveEntitlementSnapshot(context.AccountId, context.GrantId,
                Guid.NewGuid(), "premium", PlanBillingKind.OneTime, GrantSource.IndividualPurchase, context.InvitationId, null, null,
                90, 1, 100, 5, 10, 500, 600, 1000, memoriesEnabled, true, true,
                MaxGuestImages: 100, MaxGuestVideos: 10, MaxGuestImageSizeMb: imageMb, MaxGuestVideoSizeMb: 100,
                MaxGuestVideoDurationSeconds: 60)));
    }

    private sealed class Store : IGuestMediaStore
    {
        public long Images { get; init; }
        public long Videos { get; init; }
        public int Saved { get; private set; }
        public Task<GuestMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken) =>
            Task.FromResult<GuestMediaReplaySnapshot?>(null);
        public Task<(long Images, long Videos)> GetGuestUsageAsync(Guid invitationId, CancellationToken cancellationToken) =>
            Task.FromResult((Images, Videos));
        public Task<bool> SaveReservationAsync(GuestMediaReservationCommand command, Guid assetId, long maximumBytes,
            long maximumDurationSeconds, string requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Saved++;
            return Task.FromResult(true);
        }
        public Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Provider : IMediaUploadGateway, IMediaImageNormalizationPipeline
    {
        public int Calls { get; private set; }
        public bool Unsafe { get; set; }
        public bool Throw { get; set; }
        public List<MediaUploadRequest> Requests { get; } = [];

        public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
            Issue(request, new Dictionary<string, string> { ["X-Media-Capability"] = "token" });
        public Task<MediaUploadCapability> CreateVideoCapabilityAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
            Issue(request, null);
        public Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult<NormalizedImageVerificationEvidence?>(null);
        public Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult<MediaProviderVideoInspection?>(null);
        public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken) => Task.CompletedTask;

        private Task<MediaUploadCapability> Issue(MediaUploadRequest request, IReadOnlyDictionary<string, string>? headers)
        {
            Calls++;
            if (Throw) throw new HttpRequestException("down", null, HttpStatusCode.BadGateway);
            Requests.Add(request);
            return Task.FromResult(new MediaUploadCapability(
                new Uri(Unsafe ? "http://insecure.example.test/x" : "https://upload.example.test/x"), request.ExpiresAt, headers));
        }
    }
}
