using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>
/// Fails closed until the provider/edge adapter is configured. M3 replaces this adapter with a
/// bounded ingress implementation; no API or VPS-hosted byte proxy is created here.
/// </summary>
public sealed class UnavailableMediaUploadAdapter : IMediaUploadGateway, IMediaImageNormalizationPipeline
{
    public Task<MediaUploadCapability> CreateVideoCapabilityAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
        Task.FromException<MediaUploadCapability>(new NotSupportedException("Media provider ingress is not configured."));

    public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(MediaUploadRequest request, CancellationToken cancellationToken) =>
        Task.FromException<MediaUploadCapability>(new NotSupportedException("Normalized image ingress is not configured."));

    public Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken) =>
        Task.FromResult<MediaProviderVideoInspection?>(null);

    public Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken) =>
        Task.FromResult<NormalizedImageVerificationEvidence?>(null);

    public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("Media provider deletion is not configured."));
}
