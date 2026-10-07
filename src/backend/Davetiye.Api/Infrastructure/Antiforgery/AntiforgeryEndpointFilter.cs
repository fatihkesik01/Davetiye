using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Davetiye.Api.Infrastructure.Antiforgery;

/// <summary>
/// Validates the antiforgery cookie/header pair on every authenticated-cookie unsafe endpoint it is
/// applied to (docs/THREAT_MODEL.md §5: "Tüm authenticated POST/PUT/PATCH/DELETE endpoint'leri
/// antiforgery header/token doğrular"). A missing or invalid token short-circuits the pipeline with
/// 400 before the endpoint handler runs.
/// </summary>
public sealed class AntiforgeryEndpointFilter(
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Minimal API endpoint filters run after JSON body binding. If the configured header is
        // absent, Antiforgery's default form fallback would try to parse that already-consumed
        // JSON body and raise BadHttpRequestException instead of a clean validation response.
        if (string.IsNullOrWhiteSpace(options.Value.HeaderName) ||
            !context.HttpContext.Request.Headers.ContainsKey(options.Value.HeaderName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing or invalid antiforgery token.");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing or invalid antiforgery token.");
        }

        return await next(context);
    }
}
