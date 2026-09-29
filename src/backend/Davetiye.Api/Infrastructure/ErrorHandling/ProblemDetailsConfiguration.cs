namespace Davetiye.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Cross-cutting ProblemDetails customization applied to every ProblemDetails response the API
/// produces (validation failures, status-code pages, and unhandled-exception responses alike),
/// so a correlation ID is always present for incident tracing.
/// </summary>
public static class ProblemDetailsConfiguration
{
    public static void AddCorrelationId(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    }
}
