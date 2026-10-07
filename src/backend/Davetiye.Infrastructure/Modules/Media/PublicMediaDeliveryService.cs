using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Public media delivery is authorized by the current public invitation snapshot on every issuance.</summary>
public sealed class PublicMediaDeliveryService(DavetiyeDbContext db, IPublicInvitationService invitations,
    IPrivateMediaDeliveryGateway gateway, IClock clock, IOptions<CloudflareMediaOptions> options)
    : IPublicMediaDeliveryService
{
    private readonly CloudflareMediaOptions settings = options.Value;

    public async Task<MediaDeliveryResult> CreateAsync(string publicCode, Guid assetId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicCode) || assetId == Guid.Empty) return new("NotFound");
        var authorized = await invitations.GetAsync(publicCode, cancellationToken);
        if (authorized.Outcome != PublicInvitationOutcome.Active || authorized.InvitationId is not Guid invitationId ||
            authorized.Invitation?.Media?.Any(item => item.AssetId == assetId) != true) return new("NotFound");
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == assetId &&
            item.InvitationId == invitationId && item.QuotaScope == MediaQuotaScope.Creator && item.State == MediaAssetState.Ready,
            cancellationToken);
        if (asset is null) return new("NotFound");
        var now = clock.UtcNow.ToUniversalTime();
        var lifetime = asset.Kind == MediaKind.Image ? TimeSpan.FromSeconds(60) :
            TimeSpan.FromSeconds(settings.MaximumVideoPlaybackSessionSeconds);
        var requestedExpiry = now.Add(lifetime);
        (Uri Url, DateTimeOffset ExpiresAt)? issued;
        try
        {
            issued = asset.Kind == MediaKind.Image
                ? await gateway.CreateImageCapabilityAsync(assetId, requestedExpiry, cancellationToken)
                : await gateway.CreateVideoSessionAsync(assetId, requestedExpiry, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Broker transport, HTTP status, malformed JSON, and bounded-body failures are
            // expected upstream failures. Cancellation and local persistence errors must escape.
            return new("Unavailable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout is an upstream failure. A caller abort remains cancellation.
            return new("Unavailable");
        }
        if (issued is null || issued.Value.ExpiresAt <= now || issued.Value.ExpiresAt > requestedExpiry ||
            !issued.Value.Url.IsAbsoluteUri || issued.Value.Url.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(issued.Value.Url.UserInfo) || !string.IsNullOrEmpty(issued.Value.Url.Fragment))
            return new("Unavailable");

        // The provider operation can wait. Re-read the complete access/snapshot gate after it so
        // pause, expiry, ban, trash, deletion or an explicit update prevents returning stale access.
        var stillAuthorized = await invitations.GetAsync(publicCode, cancellationToken);
        if (stillAuthorized.Outcome != PublicInvitationOutcome.Active || stillAuthorized.InvitationId != invitationId ||
            stillAuthorized.Invitation?.Media?.Any(item => item.AssetId == assetId && item.Kind == asset.Kind.ToString()) != true)
            return new("NotFound");
        var stillReady = await db.MediaAssets.AsNoTracking().AnyAsync(item => item.Id == assetId &&
            item.InvitationId == invitationId && item.QuotaScope == MediaQuotaScope.Creator && item.Kind == asset.Kind &&
            item.State == MediaAssetState.Ready, cancellationToken);
        if (!stillReady) return new("NotFound");
        return new("Succeeded", asset.Kind == MediaKind.Image ? "image" : "video", issued.Value.Url, issued.Value.ExpiresAt);
    }
}
