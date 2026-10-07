using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Memories.Contracts;

namespace Davetiye.Api.Endpoints.Invitations;

/// <summary>
/// Anonymous guest memory-with-media surface (P6-M3). The upload capability is delivered only as a short-lived HttpOnly
/// cookie (never JSON, never a persistent guest token) and every failure of capability/ownership/gates is a uniform 404.
/// </summary>
public static class PublicMemoryUploadEndpoints
{
    public const string CapabilityCookieName = "__Host-davetiye-memory-upload";

    public static IEndpointRouteBuilder MapPublicMemoryUploadEndpoints(this IEndpointRouteBuilder apiV1,
        string readRateLimitPolicy, string createRateLimitPolicy, string intentRateLimitPolicy, string finalizeRateLimitPolicy)
    {
        apiV1.MapPost("/public/invitations/{publicCode}/memories/with-media", CreateAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(createRateLimitPolicy).Produces<PublicMemoryUploadSession>(201)
            .ProducesValidationProblem().Produces(403).Produces(404).Produces(409).Produces(429).Produces(503);
        apiV1.MapPost("/public/invitations/{publicCode}/memories/{memoryId:guid}/media/intents", CreateIntentAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(intentRateLimitPolicy).Produces<PublicMemoryUploadIntent>(201)
            .Produces<PublicMemoryUploadIntent>(200).ProducesValidationProblem().Produces(403).Produces(404)
            .Produces(409).Produces(429).Produces(503);
        apiV1.MapPost("/public/invitations/{publicCode}/memories/{memoryId:guid}/finalize", FinalizeAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(finalizeRateLimitPolicy).Produces<PublicMemoryFinalizeResponse>(200)
            .Produces<PublicMemoryFinalizeResponse>(202).Produces(403).Produces(404).Produces(429);
        apiV1.MapGet("/public/invitations/{publicCode}/memories/{memoryId:guid}/status", GetStatusAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicMemoryUploadStatus>().Produces(404).Produces(429);
        return apiV1;
    }

    private static async Task<IResult> CreateAsync(string publicCode, SubmitPublicMemoryRequest request, HttpContext context,
        IPublicMemoryUploadService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.CreateAsync(publicCode, request, cancellationToken);
        switch (result.Outcome)
        {
            case PublicMemoryUploadOutcome.Ok:
                // The raw token is delivered only in this HttpOnly cookie, never in JSON.
                context.Response.Cookies.Append(CapabilityCookieName, result.CapabilityToken!, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Expires = result.Session!.UploadExpiresAt,
                    IsEssential = true
                });
                return Results.Created(string.Empty, result.Session);
            case PublicMemoryUploadOutcome.Conflict:
                return Results.Conflict(new { code = result.Code });
            case PublicMemoryUploadOutcome.Invalid:
                return Results.ValidationProblem(result.Errors!);
            case PublicMemoryUploadOutcome.RateLimited:
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            case PublicMemoryUploadOutcome.UploadUnavailable:
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Media upload is temporarily unavailable.");
            default:
                return Results.NotFound();
        }
    }

    private static async Task<IResult> CreateIntentAsync(string publicCode, Guid memoryId,
        CreatePublicMemoryMediaIntentRequest request, HttpContext context, IPublicMemoryUploadService service,
        CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var idempotencyKey);
        var result = await service.CreateIntentAsync(publicCode, memoryId, context.Request.Cookies[CapabilityCookieName],
            request, idempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            PublicMemoryUploadOutcome.Ok => result.Intent!.Replayed
                ? Results.Ok(result.Intent)
                : Results.Created(string.Empty, result.Intent),
            PublicMemoryUploadOutcome.Conflict => Results.Conflict(new { code = result.Code }),
            PublicMemoryUploadOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            PublicMemoryUploadOutcome.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            PublicMemoryUploadOutcome.UploadUnavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Media upload is temporarily unavailable."),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> FinalizeAsync(string publicCode, Guid memoryId, HttpContext context,
        IPublicMemoryUploadService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.FinalizeAsync(publicCode, memoryId, context.Request.Cookies[CapabilityCookieName],
            cancellationToken);
        if (result.Outcome != PublicMemoryUploadOutcome.Ok) return Results.NotFound();
        if (result.State == PublicMemoryFinalizeState.Processing)
            return Results.Accepted(value: new PublicMemoryFinalizeResponse("processing", 0, 0));
        // The capability is consumed/revoked; clear the cookie so the browser does not keep a dead token.
        context.Response.Cookies.Delete(CapabilityCookieName, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/"
        });
        return Results.Ok(new PublicMemoryFinalizeResponse(
            result.State == PublicMemoryFinalizeState.Published ? "published" : "rejected",
            result.AcceptedMediaCount, result.RejectedMediaCount));
    }

    private static async Task<IResult> GetStatusAsync(string publicCode, Guid memoryId, HttpContext context,
        IPublicMemoryUploadService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.GetStatusAsync(publicCode, memoryId, context.Request.Cookies[CapabilityCookieName],
            cancellationToken);
        return result.Outcome == PublicMemoryUploadOutcome.Ok ? Results.Ok(result.Status) : Results.NotFound();
    }

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

/// <summary>State is "published", "processing" or "rejected" (nothing publishable remained and the memory was discarded).</summary>
public sealed record PublicMemoryFinalizeResponse(string State, int AcceptedMediaCount, int RejectedMediaCount);
