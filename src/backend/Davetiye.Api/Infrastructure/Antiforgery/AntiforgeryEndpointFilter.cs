using Microsoft.AspNetCore.Antiforgery;

namespace Davetiye.Api.Infrastructure.Antiforgery;

/// <summary>
/// Validates the antiforgery cookie/header pair on every authenticated-cookie unsafe endpoint it is
/// applied to (docs/THREAT_MODEL.md §5: "Tüm authenticated POST/PUT/PATCH/DELETE endpoint'leri
/// antiforgery header/token doğrular"). A missing or invalid token short-circuits the pipeline with
/// 400 before the endpoint handler runs.
/// </summary>
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

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
