using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

public static class CreatorMediaEndpoints
{
    public static IEndpointRouteBuilder MapCreatorMediaEndpoints(this IEndpointRouteBuilder apiV1, string rateLimitPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rateLimitPolicy);
        apiV1.MapPost("/invitations/{invitationId:guid}/media/intents", CreateIntentAsync)
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(rateLimitPolicy)
            .Produces<CreatorMediaIntentResponse>(StatusCodes.Status201Created)
            .Produces<CreatorMediaIntentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return apiV1;
    }

    private static async Task<IResult> CreateIntentAsync(
        Guid invitationId,
        CreateCreatorMediaIntentRequest request,
        HttpContext context,
        [FromServices] ICurrentAccountResolver currentAccounts,
        [FromServices] ICreatorMediaIntentService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var idempotencyKey) || idempotencyKey == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["A non-empty GUID Idempotency-Key header is required."]
            });
        }

        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.PresentationRole) || request.DeclaredByteLength <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Kind, presentation role, and positive declared byte length are required."]
            });
        }

        var identityId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(identityId, out var identityUserId))
        {
            return Results.Forbid();
        }

        var accountId = await currentAccounts.ResolveAccountIdAsync(identityUserId, cancellationToken);
        if (accountId is null)
        {
            return Results.Forbid();
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var outcome = await service.CreateAsync(new CreateCreatorMediaIntentCommand(
            accountId.Value, invitationId, idempotencyKey, request.Kind, request.PresentationRole,
            request.DeclaredByteLength), ipAddress, cancellationToken);

        if (outcome.Result is not null && outcome.Capability is not null)
        {
            var response = new CreatorMediaIntentResponse(outcome.Result.IntentId, outcome.Result.AssetId,
                outcome.Result.ExpiresAt, outcome.Result.Replayed, outcome.Capability.IngressUri,
                outcome.Capability.ExpiresAt, outcome.Capability.IngressHeaders ??
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            return outcome.Result.Replayed ? Results.Ok(response) : Results.Created((string?)null, response);
        }

        return outcome.Failure switch
        {
            CreatorMediaIntentFailure.NotFound => Results.NotFound(),
            CreatorMediaIntentFailure.AccountInactive => Results.Forbid(),
            CreatorMediaIntentFailure.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            CreatorMediaIntentFailure.IdempotencyConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "The idempotency key conflicts with an existing media intent."),
            CreatorMediaIntentFailure.IneligibleInvitation or CreatorMediaIntentFailure.InvalidGrant =>
                Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The invitation is not eligible for a media upload."),
            CreatorMediaIntentFailure.UnsupportedModule => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "The selected template does not support this media presentation."),
            CreatorMediaIntentFailure.EntitlementLimit or CreatorMediaIntentFailure.QuotaExceeded =>
                Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The media entitlement limit has been reached."),
            CreatorMediaIntentFailure.UploadUnavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Media upload is temporarily unavailable."),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["The media intent request is invalid."] })
        };
    }
}

public sealed record CreateCreatorMediaIntentRequest(MediaKind Kind, MediaPresentationRole PresentationRole, long DeclaredByteLength);

public sealed record CreatorMediaIntentResponse(
    Guid IntentId,
    Guid AssetId,
    DateTimeOffset ExpiresAt,
    bool Replayed,
    Uri IngressUri,
    DateTimeOffset CapabilityExpiresAt,
    IReadOnlyDictionary<string, string> IngressHeaders);
