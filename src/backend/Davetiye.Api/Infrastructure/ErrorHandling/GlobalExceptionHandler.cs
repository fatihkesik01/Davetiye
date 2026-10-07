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
    private const string BadRequestProblemTypeUri = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string PayloadTooLargeProblemTypeUri = "https://tools.ietf.org/html/rfc9110#section-15.5.14";

    private const string SanitizedDetail =
        "An unexpected error occurred. Please retry, and include the correlation ID if you contact support.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var malformedRequest = exception as BadHttpRequestException;
        var isMalformedRequest = malformedRequest is not null;
        if (isMalformedRequest)
        {
            logger.LogInformation(
                "Rejected malformed request. Method={Method} CorrelationId={CorrelationId}",
                httpContext.Request.Method,
                httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogError(
                "Unhandled exception. ExceptionType={ExceptionType} Method={Method} CorrelationId={CorrelationId}",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.TraceIdentifier);
        }

        var malformedStatusCode = malformedRequest?.StatusCode is >= 400 and < 500
            ? malformedRequest.StatusCode
            : StatusCodes.Status400BadRequest;
        var statusCode = isMalformedRequest ? malformedStatusCode : StatusCodes.Status500InternalServerError;
        var isBadRequest = statusCode == StatusCodes.Status400BadRequest;
        var isPayloadTooLarge = statusCode == StatusCodes.Status413PayloadTooLarge;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = isMalformedRequest
                ? isPayloadTooLarge ? "The request body is too large." : isBadRequest ? "The request is invalid." : "The request was rejected."
                : "An unexpected error occurred.",
            Type = isMalformedRequest
                ? isPayloadTooLarge ? PayloadTooLargeProblemTypeUri : isBadRequest ? BadRequestProblemTypeUri : "about:blank"
                : ProblemTypeUri,
            Detail = !isMalformedRequest
                ? environment.IsDevelopment() ? exception.Message : SanitizedDetail
                : isPayloadTooLarge ? "The request body exceeds the allowed size."
                : isBadRequest ? "The request body could not be read."
                : "The request was rejected.",
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
