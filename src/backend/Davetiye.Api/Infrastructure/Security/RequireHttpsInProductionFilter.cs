namespace Davetiye.Api.Infrastructure.Security;

/// <summary>
/// Fail-closed proof for docs/THREAT_MODEL.md §5 ("Raw-IP HTTP gerçek credential trafiğine
/// açılmaz; yalnız private smoke ve health kontrolüdür") and §12 gate 9 ("Gerçek kullanıcı auth'u
/// için HTTPS release gate'i"): in Production, any request to an auth-sensitive route group that is
/// not <see cref="HttpRequest.IsHttps"/> is rejected before the real endpoint handler runs.
///
/// By the time this filter runs, <c>Request.IsHttps</c> already reflects M4's
/// <c>UseForwardedHeaders()</c> middleware: a request genuinely proxied over HTTPS by the trusted
/// Nginx front end (docs/DEPLOYMENT.md) has its scheme correctly rewritten from
/// <c>X-Forwarded-Proto</c>, while a raw, direct, unproxied HTTP request (or one whose forwarded
/// header comes from an untrusted source, which the trusted-proxy allowlist ignores) still shows as
/// non-HTTPS here. Health endpoints are mapped outside this route group entirely, so they remain
/// reachable over raw HTTP for private smoke/health checks.
/// </summary>
public sealed class RequireHttpsInProductionFilter(IHostEnvironment environment) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (environment.IsProduction() && !context.HttpContext.Request.IsHttps)
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "HTTPS is required for authentication traffic."));
        }

        return next(context);
    }
}
