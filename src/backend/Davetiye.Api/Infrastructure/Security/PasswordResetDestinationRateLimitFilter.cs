using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Infrastructure.Security;

/// <summary>Applies the reset destination bucket after JSON binding without branching on account existence.</summary>
public sealed class PasswordResetDestinationRateLimitFilter(
    IPasswordResetDestinationRateLimiter destinationLimiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<RequestPasswordResetRequest>().FirstOrDefault();
        if (request is null || destinationLimiter.TryAcquire(request.Email))
            return await next(context);

        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests.");
    }
}
