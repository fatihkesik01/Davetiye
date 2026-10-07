using Davetiye.Infrastructure.Modules.Media;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class CloudflareMediaContentSourcesTests
{
    [Fact]
    public void Disabled_media_has_no_external_browser_origins()
    {
        var sources = new CloudflareMediaContentSources(Options.Create(new CloudflareMediaOptions()));

        Assert.Empty(sources.ImageSources);
        Assert.Empty(sources.ConnectionSources);
        Assert.Empty(sources.FrameSources);
    }

    [Fact]
    public void Enabled_media_exposes_only_the_worker_tus_and_configured_stream_origins()
    {
        var sources = new CloudflareMediaContentSources(Options.Create(new CloudflareMediaOptions
        {
            Enabled = true,
            WorkerBaseUrl = "https://media.example.test/private/ingress",
            StreamCustomerHostname = "customer-demo.cloudflarestream.com"
        }));

        Assert.Equal(["https://media.example.test"], sources.ImageSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
        Assert.Equal(["https://media.example.test", "https://upload.videodelivery.net"],
            sources.ConnectionSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
        Assert.Equal(["https://customer-demo.cloudflarestream.com"],
            sources.FrameSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
    }
}
