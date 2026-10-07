using Davetiye.Application.Modules.Media.Contracts;

namespace Davetiye.Api.Infrastructure.Security;

/// <summary>
/// Applies the minimum browser-facing response-header baseline from
/// docs/THREAT_MODEL.md §7. Values are static and do not reflect request data.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment,
    IMediaContentSources mediaContentSources)
{
    private const string BaseContentSecurityPolicy =
        "default-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-src https://www.google.com; form-action 'self'";

    private readonly string contentSecurityPolicy = BuildContentSecurityPolicy(mediaContentSources);

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.TryAdd("Content-Security-Policy", contentSecurityPolicy);
        context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
        context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
        context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");

        if (environment.IsProduction())
        {
            context.Response.Headers.TryAdd(
                "Strict-Transport-Security",
                "max-age=31536000; includeSubDomains");
        }

        return next(context);
    }

    private static string BuildContentSecurityPolicy(IMediaContentSources sources)
    {
        // These origins come from trusted, startup-validated provider configuration. Never derive
        // CSP sources from a request Host, Origin, or URL supplied by a Creator/Guest.
        if (sources.ImageSources.Count == 0 && sources.ConnectionSources.Count == 0 && sources.FrameSources.Count == 0)
            return BaseContentSecurityPolicy;

        var imageSources = string.Join(' ', sources.ImageSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
        var connectionSources = string.Join(' ', sources.ConnectionSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
        var frameSources = string.Join(' ', sources.FrameSources.Select(source => source.GetLeftPart(UriPartial.Authority)));
        return BaseContentSecurityPolicy
            .Replace("img-src 'self' data:", $"img-src 'self' data: {imageSources}".TrimEnd() + ";", StringComparison.Ordinal)
            .Replace("connect-src 'self';", $"connect-src 'self' {connectionSources};".Replace("  ", " ", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("frame-src https://www.google.com;", $"frame-src https://www.google.com {frameSources}".TrimEnd() + ";", StringComparison.Ordinal);
    }
}
