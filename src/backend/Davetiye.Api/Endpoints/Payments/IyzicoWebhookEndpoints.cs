using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Davetiye.Api.Endpoints.Payments;

public static class IyzicoWebhookEndpoints
{
    private const string SignatureHeader = "X-IYZ-SIGNATURE-V3";

    public static IEndpointRouteBuilder MapIyzicoWebhookEndpoints(this IEndpointRouteBuilder endpoints, string rateLimitPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rateLimitPolicy);
        endpoints.MapPost("/webhooks/iyzico", ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(rateLimitPolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        [FromServices] IPaymentWebhookIngestor ingestor,
        [FromServices] IOptions<RequestLimitsOptions> requestLimits,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var maximumBodyBytes = requestLimits.Value.MaxRequestBodyBytes;
        if (context.Request.ContentLength is long contentLength && contentLength > maximumBodyBytes)
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");

        var body = await ReadBoundedBodyAsync(context.Request.Body, maximumBodyBytes, cancellationToken);
        if (body is not { } boundedBody)
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");

        var signatureValues = context.Request.Headers[SignatureHeader];
        if (signatureValues.Count != 1)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Webhook signature is invalid.");

        PaymentWebhookIngestionOutcome outcome;
        try
        {
            outcome = await ingestor.IngestAsync(boundedBody, signatureValues[0], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Davetiye.Api.Payments.IyzicoWebhook")
                .LogError(exception, "Could not durably accept an iyzico webhook event.");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Payment webhook ingestion is temporarily unavailable.");
        }
        return outcome switch
        {
            PaymentWebhookIngestionOutcome.Accepted or PaymentWebhookIngestionOutcome.Duplicate => Results.Ok(),
            PaymentWebhookIngestionOutcome.InvalidPayload => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Webhook payload is invalid."),
            PaymentWebhookIngestionOutcome.InvalidSignature => Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Webhook signature is invalid."),
            PaymentWebhookIngestionOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Payment webhook ingestion is unavailable."),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Payment webhook ingestion is unavailable.")
        };
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedBodyAsync(
        Stream body, long maximumBodyBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await body.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > maximumBodyBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        return buffer.ToArray();
    }
}
