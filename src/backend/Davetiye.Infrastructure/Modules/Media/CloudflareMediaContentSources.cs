using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class CloudflareMediaContentSources(IOptions<CloudflareMediaOptions> options) : IMediaContentSources
{
    private static readonly Uri StreamUploadOrigin = new("https://upload.videodelivery.net");
    private readonly CloudflareMediaOptions settings = options.Value;

    public IReadOnlyList<Uri> ImageSources => settings.Enabled ? [WorkerOrigin()] : [];

    public IReadOnlyList<Uri> ConnectionSources => settings.Enabled ? [WorkerOrigin(), StreamUploadOrigin] : [];

    public IReadOnlyList<Uri> FrameSources => settings.Enabled ? [new Uri("https://" + settings.StreamCustomerHostname)] : [];

    private Uri WorkerOrigin()
    {
        var worker = new Uri(settings.WorkerBaseUrl, UriKind.Absolute);
        return new Uri(worker.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }
}
