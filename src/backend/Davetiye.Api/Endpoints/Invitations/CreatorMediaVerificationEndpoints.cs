using System.Security.Claims;
using System.Text.Json;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Davetiye.Api.Endpoints.Invitations;

public static class CreatorMediaVerificationEndpoints
{
    private const int MaximumWebhookBodyBytes = 64 * 1024;
    private const string SignatureHeader = "Webhook-Signature";

    public static IEndpointRouteBuilder MapCreatorMediaVerificationEndpoints(this IEndpointRouteBuilder endpoints, string rateLimitPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rateLimitPolicy);
        endpoints.MapPost("/invitations/{invitationId:guid}/media/{assetId:guid}/finalize", FinalizeImageAsync)
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(rateLimitPolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/webhooks/cloudflare/stream", StreamWebhookAsync)
            .AllowAnonymous()
            .RequireRateLimiting(rateLimitPolicy)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> FinalizeImageAsync(
        Guid invitationId,
        Guid assetId,
        HttpContext context,
        [FromServices] ICurrentAccountResolver currentAccounts,
        [FromServices] IMediaVerificationService verification,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var identityId))
            return Results.Forbid();
        var accountId = await currentAccounts.ResolveAccountIdAsync(identityId, cancellationToken);
        if (accountId is null) return Results.Forbid();

        var result = await verification.FinalizeImageAsync(accountId.Value, invitationId, assetId, cancellationToken);
        return result switch
        {
            MediaFinalizeResult.Ready or MediaFinalizeResult.Duplicate => Results.Ok(new { state = "ready" }),
            MediaFinalizeResult.Pending => Results.Accepted(value: new { state = "processing" }),
            MediaFinalizeResult.NotFound => Results.NotFound(),
            MediaFinalizeResult.Forbidden => Results.Forbid(),
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The media could not be verified.")
        };
    }

    private static async Task<IResult> StreamWebhookAsync(
        HttpContext context,
        [FromServices] IStreamWebhookSignatureVerifier signatureVerifier,
        [FromServices] IClock clock,
        [FromServices] IMediaVerificationService verification,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!signatureVerifier.IsEnabled)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Media verification is unavailable.");

        if (context.Request.ContentLength is > MaximumWebhookBodyBytes)
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        var rawBody = await ReadBoundedBodyAsync(context.Request.Body, MaximumWebhookBodyBytes, cancellationToken);
        if (rawBody is null)
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");

        var signature = context.Request.Headers[SignatureHeader].ToString();
        if (!signatureVerifier.IsValid(rawBody, signature, clock.UtcNow))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Webhook signature is invalid.");

        if (!TryReadTerminalPayload(rawBody, out var assetId, out var uid, out var state, out var readyToStream))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Webhook payload is invalid.");

        var result = await verification.ProcessStreamWebhookAsync(assetId, uid, state, readyToStream, cancellationToken);
        return result switch
        {
            MediaFinalizeResult.Pending => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Video processing verification is temporarily unavailable."),
            MediaFinalizeResult.NotFound => Results.Ok(),
            _ => Results.Ok()
        };
    }

    private static bool TryReadTerminalPayload(
        byte[] rawBody, out Guid assetId, out string uid, out string state, out bool readyToStream)
    {
        assetId = Guid.Empty;
        uid = string.Empty;
        state = string.Empty;
        readyToStream = false;
        try
        {
            using var document = JsonDocument.Parse(rawBody, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("creator", out var creator) || creator.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(creator.GetString(), "N", out assetId) ||
                !root.TryGetProperty("uid", out var uidElement) || uidElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(uid = uidElement.GetString() ?? string.Empty) || uid.Length > 512 || uid.Any(char.IsControl) ||
                !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
                !status.TryGetProperty("state", out var stateElement) || stateElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            state = stateElement.GetString() ?? string.Empty;
            if (state is not ("ready" or "error")) return false;
            if (root.TryGetProperty("readyToStream", out var ready))
            {
                if (ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                readyToStream = ready.GetBoolean();
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(Stream body, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await body.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > maximumBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        return buffer.ToArray();
    }
}
