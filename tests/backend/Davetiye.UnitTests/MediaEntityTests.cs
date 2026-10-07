using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class MediaEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_asset_can_have_both_cover_and_gallery_placements()
    {
        var asset = CreateAsset(MediaKind.Image);
        Assert.Throws<InvalidOperationException>(() => asset.Place(
            Guid.NewGuid(), MediaPresentationRole.Cover, 0, Now));
        asset.BeginProcessing("placement-image");
        asset.MarkReady(ImageEvidence("placement-image"), Now.AddMinutes(1));
        var cover = asset.Place(Guid.NewGuid(), MediaPresentationRole.Cover, 0, Now.AddMinutes(2));
        var gallery = asset.Place(Guid.NewGuid(), MediaPresentationRole.Gallery, 0, Now.AddMinutes(2));

        Assert.Equal(MediaQuotaScope.Creator, asset.QuotaScope);
        Assert.Equal(MediaAssetState.Ready, asset.State);
        Assert.Equal(asset.Id, cover.MediaAssetId);
        Assert.Equal(asset.Id, gallery.MediaAssetId);
    }

    [Fact]
    public void Ready_cannot_be_reached_without_server_inspection_after_processing()
    {
        var asset = CreateAsset(MediaKind.Image);
        var evidence = ImageEvidence("object-key/opaque-123");

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(evidence, Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.PendingUpload, asset.State);

        asset.BeginProcessing("object-key/opaque-123");
        asset.MarkReady(evidence, Now.AddMinutes(1));

        Assert.Equal(MediaAssetState.Ready, asset.State);
        Assert.Equal("image/webp", asset.DetectedContentType);
        Assert.Equal(4096L, asset.ByteLength);
        Assert.Null(typeof(MediaAsset).GetProperty("PermanentUrl"));
    }

    [Fact]
    public void Plain_provider_inspection_or_client_completion_cannot_mark_an_image_ready()
    {
        var asset = CreateAsset(MediaKind.Image);
        asset.BeginProcessing("object-key/opaque-456");
        var plainInspection = VideoEvidence("object-key/opaque-456", "image/webp", 4096, 30);

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(plainInspection, Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.Processing, asset.State);

        // This constructor is internal to the trusted normalization pipeline adapter and test assembly.
        // It cannot be model-bound from a client upload-complete request.
        asset.MarkReady(ImageEvidence("object-key/opaque-456"), Now.AddMinutes(1));
        Assert.Equal(MediaAssetState.Ready, asset.State);
    }

    [Fact]
    public void Ready_requires_media_kind_to_match_authoritatively_detected_type()
    {
        var asset = CreateAsset(MediaKind.Video);
        asset.BeginProcessing("opaque-video-id");
        var evidence = VideoEvidence("opaque-video-id", "image/webp", 500, 45);

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(evidence, Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.Processing, asset.State);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/javascript")]
    public void Normalized_image_ready_state_rejects_active_content(string contentType)
    {
        var asset = CreateAsset(MediaKind.Image);
        asset.BeginProcessing("normalized-object");

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(
            new NormalizedImageVerificationEvidence("normalized-object", contentType, 100), Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.Processing, asset.State);
    }

    [Fact]
    public void Video_ready_state_rejects_nonvideo_active_content()
    {
        var asset = CreateAsset(MediaKind.Video);
        asset.BeginProcessing("opaque-video-id");

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(
            VideoEvidence("opaque-video-id", "text/html", 500, 45), Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.Processing, asset.State);
    }

    [Fact]
    public void Provider_object_reference_rejects_permanent_URLs()
    {
        var asset = CreateAsset(MediaKind.Video);

        Assert.Throws<ArgumentException>(() => asset.BeginProcessing("https://storage.example/object"));
        Assert.Equal(MediaAssetState.PendingUpload, asset.State);
    }

    [Fact]
    public void Trash_compatibility_keeps_asset_until_explicit_provider_deletion_confirmation()
    {
        var asset = CreateAsset(MediaKind.Image);
        asset.BeginProcessing("opaque-image-id");
        asset.MarkReady(ImageEvidence("opaque-image-id"), Now.AddMinutes(1));

        asset.RequestDeletion(Now.AddHours(1));

        Assert.Equal(MediaAssetState.PendingDeletion, asset.State);
        Assert.Null(asset.ProviderDeletedAt);
        asset.ConfirmProviderDeletion(Now.AddHours(2));
        Assert.Equal(MediaAssetState.Deleted, asset.State);
        Assert.Equal(Now.AddHours(2), asset.ProviderDeletedAt);
    }

    [Fact]
    public void Pending_upload_is_open_until_expiry_consumption_or_cancellation()
    {
        var upload = CreateUpload();
        upload.Consume(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(2), upload.ConsumedAt);
        Assert.Throws<InvalidOperationException>(() => upload.Consume(Now.AddMinutes(3)));
    }

    [Fact]
    public void Expired_intent_must_be_explicitly_closed_before_a_retry()
    {
        var upload = CreateUpload();

        Assert.Throws<ArgumentException>(() => upload.CancelExpired(Now.AddMinutes(9)));
        upload.CancelExpired(Now.AddMinutes(10));
        Assert.Equal(Now.AddMinutes(10), upload.CancelledAt);
    }

    [Fact]
    public void Normalized_image_evidence_is_not_publicly_constructible_or_client_bindable()
    {
        var publicConstructors = typeof(NormalizedImageVerificationEvidence).GetConstructors();
        Assert.Empty(publicConstructors);
        Assert.Null(typeof(NormalizedImageVerificationEvidence).GetMethod("FromServerInspection"));

        var asset = CreateAsset(MediaKind.Image);
        asset.BeginProcessing("object-key/opaque-789");
        var plainProviderInspection = VideoEvidence("object-key/opaque-789", "image/webp", 4096, 45);

        Assert.Throws<InvalidOperationException>(() => asset.MarkReady(plainProviderInspection, Now.AddMinutes(1)));
        Assert.Equal(MediaAssetState.Processing, asset.State);
    }

    [Theory]
    [InlineData(EntitlementCatalog.MaxImages, 250)]
    [InlineData(EntitlementCatalog.MaxVideos, 10)]
    [InlineData(EntitlementCatalog.MaxImageSizeMb, 10)]
    [InlineData(EntitlementCatalog.MaxVideoSizeMb, 1000)]
    [InlineData(EntitlementCatalog.MaxVideoDurationSeconds, 900)]
    public void Media_entitlement_hard_ceilings_match_accepted_commercial_maxima(string key, long maximum)
    {
        var definition = EntitlementCatalog.Require(key);

        Assert.Equal(maximum, definition.HardCeiling);
        Assert.NotNull(PlanEntitlement.Create(Guid.NewGuid(), Guid.NewGuid(), key, maximum, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlement.Create(
            Guid.NewGuid(), Guid.NewGuid(), key, maximum + 1, null));
    }

    private static MediaAsset CreateAsset(MediaKind kind) =>
        MediaAsset.CreateCreatorAsset(Guid.NewGuid(), Guid.NewGuid(), kind, Now);

    private static PendingUpload CreateUpload() => PendingUpload.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MediaPresentationRole.Gallery,
        512, new string('a', 64), Now, Now.AddMinutes(10));

    private static NormalizedImageVerificationEvidence ImageEvidence(string reference) =>
        new(reference, "image/webp", 4096);

    private static MediaVerificationEvidence VideoEvidence(
        string reference, string contentType, long byteLength, int durationSeconds) =>
        new(reference, contentType, byteLength, durationSeconds);
}

