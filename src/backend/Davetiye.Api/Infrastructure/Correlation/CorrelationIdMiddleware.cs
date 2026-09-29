namespace Davetiye.Api.Infrastructure.Correlation;

/// <summary>
/// Assigns every request a correlation ID: the caller-supplied "X-Correlation-Id" header when it
/// is present and well-formed, otherwise a newly generated one. The ID is stored on
/// <see cref="HttpContext.TraceIdentifier"/> (so it flows into logging scopes and into
/// ProblemDetails, which already surfaces TraceIdentifier-derived data) and echoed back on the
/// response so incidents can be traced end to end, per docs/THREAT_MODEL.md.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxIncomingLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        await next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var candidate = values.ToString();
            if (IsValidIncomingCorrelationId(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValidIncomingCorrelationId(string value) =>
        value.Length is > 0 and <= MaxIncomingLength &&
        value.All(static character => char.IsLetterOrDigit(character) || character is '-' or '_');
}

public static class CorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
