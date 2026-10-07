using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Api.Infrastructure.Security;
using System.Text.Encodings.Web;
using System.Text.Json;
using Davetiye.Application.Modules.Memories.Contracts;

namespace Davetiye.Api.Endpoints.Invitations;

public static class PublicMemoriesEndpoints
{
    public const int MaxPageSize = 50;

    /// <summary>Guest-authored text is always emitted with the strict default encoder (escapes &lt; &gt; &amp; and quotes), as defense in depth.</summary>
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Default };

    public static IEndpointRouteBuilder MapPublicMemoriesEndpoints(this IEndpointRouteBuilder apiV1,
        string readRateLimitPolicy, string submitRateLimitPolicy, string mediaDeliveryRateLimitPolicy)
    {
        apiV1.MapGet("/public/invitations/{publicCode}/memories/configuration", GetConfigurationAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicMemoriesConfiguration>().Produces(404).Produces(429);
        apiV1.MapGet("/public/invitations/{publicCode}/memories", ListAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicMemoriesPage>().ProducesValidationProblem()
            .Produces(404).Produces(429);
        apiV1.MapPost("/public/invitations/{publicCode}/memories", SubmitAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(submitRateLimitPolicy).Produces<PublicMemorySubmissionResponse>(201)
            .ProducesValidationProblem().Produces(403).Produces(404).Produces(409).Produces(429);
        apiV1.MapPost("/public/invitations/{publicCode}/memories/{memoryId:guid}/media/{assetId:guid}/delivery", CreateMediaDeliveryAsync)
            .AllowAnonymous().WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(mediaDeliveryRateLimitPolicy).Produces<PublicMemoryMediaDeliveryResponse>()
            .Produces(400).Produces(403).Produces(404).Produces(429).Produces(503);
        return apiV1;
    }

    private static async Task<IResult> GetConfigurationAsync(string publicCode, HttpContext context,
        IPublicMemoriesService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.GetConfigurationAsync(publicCode, cancellationToken);
        return result.Outcome == PublicMemoryOutcome.Available ? Results.Ok(result.Configuration) : Results.NotFound();
    }

    private static async Task<IResult> ListAsync(string publicCode, HttpContext context, IPublicMemoriesService service,
        CancellationToken cancellationToken, long page = 1, int pageSize = 20)
    {
        SetPrivateHeaders(context);
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [page < 1 ? "page" : "pageSize"] = [page < 1 ? "Page must be at least 1." : $"Page size must be between 1 and {MaxPageSize}."]
            });
        var result = await service.ListAsync(publicCode, (int)Math.Min(page, int.MaxValue), pageSize, cancellationToken);
        return result.Outcome == PublicMemoryOutcome.Available ? Results.Json(result.Page, StrictJson) : Results.NotFound();
    }

    private static async Task<IResult> SubmitAsync(string publicCode, SubmitPublicMemoryRequest request,
        HttpContext context, IPublicMemoriesService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.SubmitAsync(publicCode, request, cancellationToken);
        return result.Outcome switch
        {
            PublicMemoryOutcome.Available => Results.Created(string.Empty, result.Memory),
            PublicMemoryOutcome.QuotaReached => Results.Conflict(new { code = "memory_quota_reached" }),
            PublicMemoryOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            PublicMemoryOutcome.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> CreateMediaDeliveryAsync(string publicCode, Guid memoryId, Guid assetId,
        HttpContext context, IPublicMemoriesService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.CreateMediaDeliveryAsync(publicCode, memoryId, assetId, cancellationToken);
        return result.Outcome switch
        {
            PublicMemoryOutcome.Available => Results.Ok(new PublicMemoryMediaDeliveryResponse(result.MediaKind!, result.Url!, result.ExpiresAt!.Value)),
            PublicMemoryOutcome.Unavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.NotFound()
        };
    }

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed record PublicMemoryMediaDeliveryResponse(string Kind, Uri Url, DateTimeOffset ExpiresAt);
