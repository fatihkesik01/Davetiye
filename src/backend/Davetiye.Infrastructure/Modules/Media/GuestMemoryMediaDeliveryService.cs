using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Guest-memory provider delivery stays inside Media and only signs invitation-owned Ready Guest assets.</summary>
public sealed class GuestMemoryMediaDeliveryService(DavetiyeDbContext db, IPrivateMediaDeliveryGateway gateway,
    IClock clock, IOptions<CloudflareMediaOptions> options) : IGuestMemoryMediaDeliveryService
{
    private readonly CloudflareMediaOptions settings = options.Value;

    public async Task<MediaDeliveryResult> CreateAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty || assetId == Guid.Empty) return new("NotFound");
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == assetId &&
            item.InvitationId == invitationId && item.QuotaScope == MediaQuotaScope.Guest && item.State == MediaAssetState.Ready,
            cancellationToken);
        if (asset is null) return new("NotFound");

        var now = clock.UtcNow.ToUniversalTime();
        var requestedExpiry = now.Add(asset.Kind == MediaKind.Image
            ? TimeSpan.FromSeconds(60)
            : TimeSpan.FromSeconds(settings.MaximumVideoPlaybackSessionSeconds));
        (Uri Url, DateTimeOffset ExpiresAt)? issued;
        try
        {
            issued = asset.Kind == MediaKind.Image
                ? await gateway.CreateImageCapabilityAsync(assetId, requestedExpiry, cancellationToken)
                : await gateway.CreateVideoSessionAsync(assetId, requestedExpiry, cancellationToken);
        }
        catch (HttpRequestException) { return new("Unavailable"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("Unavailable"); }

        if (issued is null || issued.Value.ExpiresAt <= now || issued.Value.ExpiresAt > requestedExpiry ||
            !issued.Value.Url.IsAbsoluteUri || issued.Value.Url.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(issued.Value.Url.UserInfo) || !string.IsNullOrEmpty(issued.Value.Url.Fragment))
            return new("Unavailable");

        // Provider signing can wait; refuse to return a capability after concurrent rejection/deletion.
        if (!await IsReadyForInvitationAsync(invitationId, assetId, cancellationToken)) return new("NotFound");
        return new("Succeeded", asset.Kind == MediaKind.Image ? "image" : "video", issued.Value.Url, issued.Value.ExpiresAt);
    }

    public Task<bool> IsReadyForInvitationAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken) =>
        db.MediaAssets.AsNoTracking().AnyAsync(item => item.Id == assetId && item.InvitationId == invitationId &&
            item.QuotaScope == MediaQuotaScope.Guest && item.State == MediaAssetState.Ready, cancellationToken);
}
