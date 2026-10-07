using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

/// <summary>Creator Memories configuration and moderation surfaces.</summary>
public static class CreatorMemoriesEndpoints
{
    public static IEndpointRouteBuilder MapCreatorMemoriesEndpoints(this IEndpointRouteBuilder endpoints,
        string readRateLimitPolicy, string writeRateLimitPolicy)
    {
        var group = endpoints.MapGroup("/invitations/{invitationId:guid}/memories")
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        group.MapGet("/configuration", GetAsync).RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorMemoryConfiguration>().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/configuration", UpdateAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorMemoryConfiguration>().ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("", ListAsync).RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorMemoriesPage>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/{memoryId:guid}/hide", HideAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapDelete("/{memoryId:guid}", DeleteAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/{memoryId:guid}/media/{assetId:guid}/delivery", CreateMediaDeliveryAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorMemoryPreviewResponse>().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests).Produces(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static Task<IResult> ListAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver, [FromServices] ICreatorMemoriesService service,
        [FromServices] ICreatorMemoriesRateLimiter rateLimiter, CancellationToken cancellationToken,
        int page = 1, int pageSize = 25)
    {
        SetPrivateHeaders(context);
        if (page < 1 || pageSize is < 1 or > 100)
            return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [page < 1 ? "page" : "pageSize"] = [page < 1 ? "Page must be at least 1." : "Page size must be between 1 and 100."]
            }));
        return ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Read, cancellationToken,
            accountId => service.ListAsync(accountId, invitationId, page, pageSize, cancellationToken),
            result => result.Outcome == CreatorMemoriesOutcome.Succeeded ? Results.Ok(result.Page) : Results.NotFound());
    }

    private static Task<IResult> HideAsync(Guid invitationId, Guid memoryId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver, [FromServices] ICreatorMemoriesService service,
        [FromServices] ICreatorMemoriesRateLimiter rateLimiter, CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Write, cancellationToken,
            accountId => service.HideAsync(accountId, invitationId, memoryId, cancellationToken), ToModerationResult);

    private static Task<IResult> DeleteAsync(Guid invitationId, Guid memoryId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver, [FromServices] ICreatorMemoriesService service,
        [FromServices] ICreatorMemoriesRateLimiter rateLimiter, CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Write, cancellationToken,
            accountId => service.DeleteAsync(accountId, invitationId, memoryId, cancellationToken), ToModerationResult);

    private static Task<IResult> CreateMediaDeliveryAsync(Guid invitationId, Guid memoryId, Guid assetId,
        HttpContext context, [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorMemoriesService service, [FromServices] ICreatorMemoriesRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Read, cancellationToken,
            accountId => service.CreateMediaDeliveryAsync(accountId, invitationId, memoryId, assetId, cancellationToken),
            result => result.Outcome switch
            {
                CreatorMemoriesOutcome.Succeeded => Results.Ok(new CreatorMemoryPreviewResponse(result.MediaKind!, result.Url!, result.ExpiresAt!.Value)),
                CreatorMemoriesOutcome.Unavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
                _ => Results.NotFound()
            });

    private static IResult ToModerationResult(CreatorMemoryModerationResult result) => result.Outcome switch
    {
        CreatorMemoriesOutcome.Succeeded => Results.NoContent(),
        CreatorMemoriesOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "The memory is not in a state that can be hidden."),
        _ => Results.NotFound()
    };

    private static IResult ToModerationResult(CreatorMemoriesListResult result) =>
        result.Outcome == CreatorMemoriesOutcome.Succeeded ? Results.Ok(result.Page) : Results.NotFound();

    private static Task<IResult> GetAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver, [FromServices] ICreatorMemoryConfigurationService service,
        [FromServices] ICreatorMemoriesRateLimiter rateLimiter, CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Read, cancellationToken,
            accountId => service.GetAsync(accountId, invitationId, cancellationToken));

    private static Task<IResult> UpdateAsync(Guid invitationId, UpdateMemoryConfigurationRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorMemoryConfigurationService service, [FromServices] ICreatorMemoriesRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, rateLimiter, CreatorMemoriesRateLimitBucket.Write, cancellationToken,
            accountId => service.UpdateAsync(accountId, invitationId, request, cancellationToken));

    private static async Task<IResult> ExecuteAsync(HttpContext context, ICurrentAccountResolver resolver,
        ICreatorMemoriesRateLimiter rateLimiter, CreatorMemoriesRateLimitBucket bucket, CancellationToken cancellationToken,
        Func<Guid, Task<CreatorMemoryConfigurationResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue))
            return Results.Forbid();
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var identityUserId)) return Results.Forbid();
        var accountId = await resolver.ResolveAccountIdAsync(identityUserId, cancellationToken);
        if (accountId is null) return Results.Forbid();
        if (!rateLimiter.TryAcquire(accountId.Value, bucket)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        var result = await action(accountId.Value);
        return result.Outcome switch
        {
            CreatorMemoryConfigurationOutcome.Succeeded => Results.Ok(result.Configuration),
            CreatorMemoryConfigurationOutcome.NotFound => Results.NotFound(),
            CreatorMemoryConfigurationOutcome.Invalid => Results.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()),
            CreatorMemoryConfigurationOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Memories configuration was updated elsewhere.",
                extensions: new Dictionary<string, object?> { ["currentRevision"] = result.CurrentRevision }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, ICurrentAccountResolver resolver,
        ICreatorMemoriesRateLimiter rateLimiter, CreatorMemoriesRateLimitBucket bucket, CancellationToken cancellationToken,
        Func<Guid, Task<T>> action, Func<T, IResult> toResult)
    {
        SetPrivateHeaders(context);
        if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue))
            return Results.Forbid();
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var identityUserId)) return Results.Forbid();
        var accountId = await resolver.ResolveAccountIdAsync(identityUserId, cancellationToken);
        if (accountId is null) return Results.Forbid();
        if (!rateLimiter.TryAcquire(accountId.Value, bucket)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        return toResult(await action(accountId.Value));
    }

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
