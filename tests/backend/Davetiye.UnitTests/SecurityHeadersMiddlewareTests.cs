using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Production_response_sets_the_required_browser_security_headers()
    {
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            new TestHostEnvironment("Production"),
            TestMediaContentSources.Empty);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Contains("object-src 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-src https://www.google.com;", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("maps.google.com", policy, StringComparison.Ordinal);
        Assert.Equal("max-age=31536000; includeSubDomains", context.Response.Headers["Strict-Transport-Security"].ToString());
    }

    [Fact]
    public async Task Enabled_media_adds_only_configured_delivery_and_upload_origins_to_csp()
    {
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            new TestHostEnvironment("Development"),
            new TestMediaContentSources(
                [new Uri("https://media.example.test")],
                [new Uri("https://media.example.test"), new Uri("https://upload.videodelivery.net")],
                [new Uri("https://customer-123.cloudflarestream.com")]));
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = "https://untrusted.example.test";

        await middleware.InvokeAsync(context);

        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("img-src 'self' data: https://media.example.test;", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://media.example.test https://upload.videodelivery.net;", policy, StringComparison.Ordinal);
        Assert.Contains("frame-src https://www.google.com https://customer-123.cloudflarestream.com;", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("untrusted.example.test", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("/ingress", policy, StringComparison.Ordinal);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Davetiye.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestMediaContentSources(IReadOnlyList<Uri> images,
        IReadOnlyList<Uri> connections, IReadOnlyList<Uri> frames) : IMediaContentSources
    {
        public static readonly TestMediaContentSources Empty = new([], [], []);

        public IReadOnlyList<Uri> ImageSources { get; } = images;
        public IReadOnlyList<Uri> ConnectionSources { get; } = connections;
        public IReadOnlyList<Uri> FrameSources { get; } = frames;
    }
}
