using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Converts any unhandled exception into a sanitized RFC 7807 ProblemDetails response. In
/// non-Development environments the response never contains the exception message or a stack
/// trace, per docs/THREAT_MODEL.md §7 ("ProblemDetails private stack, SQL/provider payload veya
/// secret içermez"). The full exception is still logged server-side, tagged with the same
/// correlation ID returned to the client, so incidents remain traceable without leaking detail
/// to the caller.
/// </summary>
public sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const string ProblemTypeUri = "https://tools.ietf.org/html/rfc9110#section-15.6.1";

    private const string SanitizedDetail =
        "An unexpected error occurred. Please retry, and include the correlation ID if you contact support.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogError(
            "Unhandled exception. ExceptionType={ExceptionType} Method={Method} CorrelationId={CorrelationId}",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.TraceIdentifier);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = ProblemTypeUri,
            Detail = environment.IsDevelopment() ? exception.Message : SanitizedDetail,
            Instance = httpContext.Request.Path,
        };
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });

        return true;
    }
}
