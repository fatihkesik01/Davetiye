using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Primitives;

namespace Davetiye.Api.Infrastructure.Antiforgery;

/// <summary>Validates the cookie + hidden-field token pair on native HTML form POSTs.</summary>
public sealed class FormAntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        if (!string.Equals(httpContext.Request.ContentType?.Split(';', 2)[0].Trim(),
                "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A valid same-origin form token is required.");
        }

        var originHeader = httpContext.Request.Headers.Origin;
        if (!StringValues.IsNullOrEmpty(originHeader) &&
            (!Uri.TryCreate(originHeader.ToString(), UriKind.Absolute, out var origin) ||
             !string.Equals(origin.Scheme, httpContext.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(origin.Authority, httpContext.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A valid same-origin form token is required.");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A valid same-origin form token is required.");
        }

        return await next(context);
    }
}
