namespace Davetiye.Api.Infrastructure.Security;

/// <summary>
/// Applies the minimum browser-facing response-header baseline from
/// docs/THREAT_MODEL.md §7. Values are static and do not reflect request data.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
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
}
