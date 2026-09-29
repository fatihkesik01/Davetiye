using Davetiye.Api.Infrastructure.Security;
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
            new TestHostEnvironment("Production"));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);
        await context.Response.CompleteAsync();

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Contains("object-src 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Equal("max-age=31536000; includeSubDomains", context.Response.Headers["Strict-Transport-Security"].ToString());
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Davetiye.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
