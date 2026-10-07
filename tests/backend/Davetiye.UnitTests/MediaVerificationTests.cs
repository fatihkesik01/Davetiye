using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Media;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Media;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class MediaVerificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Secret = "stream-webhook-test-secret-with-enough-entropy";

    [Fact]
    public void Stream_signature_covers_raw_bytes_and_accepts_current_timestamp()
    {
        var body = Encoding.UTF8.GetBytes("{\"uid\":\"video-1\"}\n");
        var header = Sign(body, Now.ToUnixTimeSeconds());

        Assert.True(StreamWebhookSignatureVerifier.Verify(body, header, Secret, Now, TimeSpan.FromMinutes(5)));
        Assert.False(StreamWebhookSignatureVerifier.Verify(Encoding.UTF8.GetBytes("{\"uid\":\"video-1\"}"),
            header, Secret, Now, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(31)]
    public void Stream_signature_rejects_stale_or_future_timestamp(long deltaSeconds)
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var header = Sign(body, Now.ToUnixTimeSeconds() + deltaSeconds);
        Assert.False(StreamWebhookSignatureVerifier.Verify(body, header, Secret, Now, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData("time=1,sig1=xx")]
    [InlineData("time=1,sig1=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,extra=x")]
    [InlineData("time=1,time=1,sig1=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Stream_signature_rejects_malformed_header(string header) =>
        Assert.False(StreamWebhookSignatureVerifier.Verify([], header, Secret, Now, TimeSpan.FromMinutes(5)));

    [Fact]
    public async Task Image_becomes_ready_only_from_normalized_webp_and_actual_bytes_within_entitlement()
    {
        var store = new FakeStore(Reservation(MediaKind.Image, maximumBytes: 10_000, declaredBytes: 5_000));
        var pipeline = new FakeImagePipeline(new NormalizedImageVerificationEvidence("creator/image.webp", "image/webp", 4_000));
        var service = Service(store, pipeline, new FakeVideoGateway(null));

        var result = await service.FinalizeImageAsync(Guid.NewGuid(), store.Reservation.InvitationId,
            store.Reservation.AssetId, CancellationToken.None);

        Assert.Equal(MediaFinalizeResult.Ready, result);
        Assert.False(store.Rejected);
        Assert.Equal(4_000, store.ImageEvidence!.ByteLength);
    }

    [Fact]
    public async Task Image_rejects_active_content_or_bytes_over_the_declared_size()
    {
        foreach (var evidence in new[]
                 {
                     new NormalizedImageVerificationEvidence("asset", "image/svg+xml", 100),
                     new NormalizedImageVerificationEvidence("asset", "image/webp", 5_001),
                 })
        {
            var store = new FakeStore(Reservation(MediaKind.Image, maximumBytes: 10_000, declaredBytes: 5_000));
            var service = Service(store, new FakeImagePipeline(evidence), new FakeVideoGateway(null));
            var result = await service.FinalizeImageAsync(Guid.NewGuid(), store.Reservation.InvitationId,
                store.Reservation.AssetId, CancellationToken.None);
            Assert.Equal(MediaFinalizeResult.Rejected, result);
            Assert.True(store.Rejected);
        }
    }

    [Fact]
    public async Task Video_requires_matching_provider_uid_and_inspected_hard_byte_duration_limits()
    {
        var reservation = Reservation(MediaKind.Video, maximumBytes: 2_000, declaredBytes: 1_500, maximumDuration: 30);
        var inspection = new MediaProviderVideoInspection("stream-uid", "ready", true, 1_000, 25, "video/mp4",
            new MediaVerificationEvidence("stream-uid", "video/mp4", 1_000, 25));
        var store = new FakeStore(reservation);
        var service = Service(store, new FakeImagePipeline(null), new FakeVideoGateway(inspection));

        var result = await service.ProcessStreamWebhookAsync(reservation.AssetId, "different-uid", "ready", true,
            CancellationToken.None);
        Assert.Equal(MediaFinalizeResult.Pending, result);
        Assert.Null(store.VideoEvidence);

        result = await service.ProcessStreamWebhookAsync(reservation.AssetId, "stream-uid", "ready", true,
            CancellationToken.None);
        Assert.Equal(MediaFinalizeResult.Ready, result);
        Assert.Equal("stream-uid", store.VideoEvidence!.ProviderObjectReference);
    }

    [Theory]
    [InlineData(1501, 10, "video/mp4")]
    [InlineData(1000, 31, "video/mp4")]
    [InlineData(1000, 10, "text/html")]
    [InlineData(1000, 10, "image/svg+xml")]
    public async Task Video_rejects_inspected_bytes_duration_or_active_content_outside_the_allowlist(
        long bytes, int duration, string contentType)
    {
        var reservation = Reservation(MediaKind.Video, maximumBytes: 2_000, declaredBytes: 1_500, maximumDuration: 30);
        var evidence = new MediaVerificationEvidence("stream-uid", contentType, bytes, duration);
        var inspection = new MediaProviderVideoInspection("stream-uid", "ready", true, bytes, duration, contentType, evidence);
        var store = new FakeStore(reservation);
        var service = Service(store, new FakeImagePipeline(null), new FakeVideoGateway(inspection));

        var result = await service.ProcessStreamWebhookAsync(reservation.AssetId, "stream-uid", "ready", true,
            CancellationToken.None);

        Assert.Equal(MediaFinalizeResult.Rejected, result);
        Assert.True(store.Rejected);
    }

    [Fact]
    public async Task Guest_video_is_verified_by_pulling_provider_inspection_within_the_reserved_limits()
    {
        var reservation = Reservation(MediaKind.Video, maximumBytes: 2_000, declaredBytes: 1_500, maximumDuration: 30);
        var inspection = new MediaProviderVideoInspection("stream-uid", "ready", true, 1_000, 25, "video/mp4",
            new MediaVerificationEvidence("stream-uid", "video/mp4", 1_000, 25));
        var store = new FakeStore(reservation);
        IGuestMediaVerificationService service = Service(store, new FakeImagePipeline(null), new FakeVideoGateway(inspection));

        Assert.Equal(MediaFinalizeResult.NotFound, await service.VerifyAsync(Guid.NewGuid(), reservation.AssetId, CancellationToken.None));
        Assert.Equal(MediaFinalizeResult.Ready, await service.VerifyAsync(reservation.InvitationId, reservation.AssetId, CancellationToken.None));
        Assert.Equal("stream-uid", store.VideoEvidence!.ProviderObjectReference);
    }

    [Theory]
    [InlineData("inprogress", false, MediaFinalizeResult.Pending)]
    [InlineData("error", false, MediaFinalizeResult.Rejected)]
    public async Task Guest_video_that_is_not_ready_stays_pending_and_provider_errors_reject(string state, bool ready, MediaFinalizeResult expected)
    {
        var reservation = Reservation(MediaKind.Video, maximumBytes: 2_000, declaredBytes: 1_500, maximumDuration: 30);
        var inspection = new MediaProviderVideoInspection("stream-uid", state, ready, null, null, null, null);
        var store = new FakeStore(reservation);
        IGuestMediaVerificationService service = Service(store, new FakeImagePipeline(null), new FakeVideoGateway(inspection));

        Assert.Equal(expected, await service.VerifyAsync(reservation.InvitationId, reservation.AssetId, CancellationToken.None));
        Assert.Equal(expected == MediaFinalizeResult.Rejected, store.Rejected);
    }

    [Fact]
    public async Task Guest_video_over_reserved_duration_is_rejected_and_missing_inspection_stays_pending()
    {
        var reservation = Reservation(MediaKind.Video, maximumBytes: 2_000, declaredBytes: 1_500, maximumDuration: 30);
        var over = new MediaProviderVideoInspection("stream-uid", "ready", true, 1_000, 31, "video/mp4",
            new MediaVerificationEvidence("stream-uid", "video/mp4", 1_000, 31));
        var rejectingStore = new FakeStore(reservation);
        IGuestMediaVerificationService rejecting = Service(rejectingStore, new FakeImagePipeline(null), new FakeVideoGateway(over));
        Assert.Equal(MediaFinalizeResult.Rejected, await rejecting.VerifyAsync(reservation.InvitationId, reservation.AssetId, CancellationToken.None));
        Assert.True(rejectingStore.Rejected);

        IGuestMediaVerificationService pending = Service(new FakeStore(reservation), new FakeImagePipeline(null), new FakeVideoGateway(null));
        Assert.Equal(MediaFinalizeResult.Pending, await pending.VerifyAsync(reservation.InvitationId, reservation.AssetId, CancellationToken.None));
    }

    [Fact]
    public async Task Guest_image_uses_the_same_normalized_webp_evidence_path_as_creator_images()
    {
        var reservation = Reservation(MediaKind.Image, maximumBytes: 10_000, declaredBytes: 5_000);
        var store = new FakeStore(reservation);
        IGuestMediaVerificationService service = Service(store, new FakeImagePipeline(
            new NormalizedImageVerificationEvidence("creator/image.webp", "image/webp", 4_000)), new FakeVideoGateway(null));
        Assert.Equal(MediaFinalizeResult.Ready, await service.VerifyAsync(reservation.InvitationId, reservation.AssetId, CancellationToken.None));
        Assert.Equal(4_000, store.ImageEvidence!.ByteLength);
    }

    private static MediaVerificationService Service(FakeStore store, FakeImagePipeline images, FakeVideoGateway videos) =>
        new(store, new FakeInvitationReader(), images, videos, new FixedClock(Now));

    private static MediaVerificationReservation Reservation(MediaKind kind, long maximumBytes, long declaredBytes, long maximumDuration = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid(), kind, MediaAssetState.PendingUpload, null,
            declaredBytes, maximumBytes, maximumDuration, Now.AddMinutes(15));

    private static string Sign(byte[] body, long timestamp)
    {
        var prefix = Encoding.ASCII.GetBytes($"{timestamp}.");
        var payload = prefix.Concat(body).ToArray();
        return $"time={timestamp},sig1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), payload)).ToLowerInvariant()}";
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeStore(MediaVerificationReservation reservation) : IMediaVerificationStore
    {
        public MediaVerificationReservation Reservation { get; } = reservation;
        public bool Rejected { get; private set; }
        public MediaVerificationEvidence? VideoEvidence { get; private set; }
        public NormalizedImageVerificationEvidence? ImageEvidence { get; private set; }
        public Task<MediaVerificationReservation?> LoadCreatorAssetAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult<MediaVerificationReservation?>(assetId == Reservation.AssetId && invitationId == Reservation.InvitationId ? Reservation : null);
        public Task<MediaVerificationReservation?> LoadAssetAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult<MediaVerificationReservation?>(assetId == Reservation.AssetId ? Reservation : null);
        public Task<MediaVerificationReservation?> LoadGuestAssetAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult<MediaVerificationReservation?>(assetId == Reservation.AssetId && invitationId == Reservation.InvitationId ? Reservation : null);
        public Task<bool> IsProviderEventProcessedAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CompleteAsync(Guid assetId, string providerObjectReference, MediaVerificationEvidence? videoEvidence,
            NormalizedImageVerificationEvidence? imageEvidence, string? providerEventId, DateTimeOffset now, bool rejected,
            CancellationToken cancellationToken)
        {
            Rejected = rejected;
            VideoEvidence = videoEvidence;
            ImageEvidence = imageEvidence;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeInvitationReader : ICreatorMediaInvitationAccessReader
    {
        public Task<CreatorMediaInvitationAccessSnapshot?> LoadAsync(Guid accountId, Guid invitationId,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<CreatorMediaInvitationAccessSnapshot?>(new(true, Guid.NewGuid(), "template"));
    }

    private sealed class FakeImagePipeline(NormalizedImageVerificationEvidence? evidence) : IMediaImageNormalizationPipeline
    {
        public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult(evidence);
    }

    private sealed class FakeVideoGateway(MediaProviderVideoInspection? inspection) : IMediaUploadGateway
    {
        public Task<MediaUploadCapability> CreateVideoCapabilityAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken) => Task.FromResult(inspection);
        public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
