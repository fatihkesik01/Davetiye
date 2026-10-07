using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Davetiye.Api.Endpoints.Invitations;

public static class CreatorRsvpEndpoints
{
    public static IEndpointRouteBuilder MapCreatorRsvpEndpoints(this IEndpointRouteBuilder endpoints,
        string readRateLimitPolicy, string writeRateLimitPolicy)
    {
        var group = endpoints.MapGroup("/invitations/{invitationId:guid}/rsvp")
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        group.MapGet("", GetAsync).RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorRsvpConfiguration>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("", SetEnabledAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorRsvpConfiguration>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests);
        group.MapPost("/questions", AddQuestionAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorRsvpConfiguration>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/questions/{questionId:guid}", UpdateQuestionAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorRsvpConfiguration>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests);
        group.MapDelete("/questions/{questionId:guid}", ArchiveQuestionAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorRsvpConfiguration>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests);
        group.MapPut("/questions/order", ReorderQuestionsAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces<CreatorRsvpConfiguration>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("/submissions", ListSubmissionsAsync).RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorRsvpSubmissionPage>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("/submissions/{submissionId:guid}", GetSubmissionAsync).RequireRateLimiting(readRateLimitPolicy)
            .Produces<CreatorRsvpSubmissionDetail>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests);
        group.MapDelete("/submissions/{submissionId:guid}", DeleteSubmissionAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(writeRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests);
        return endpoints;
    }

    private static Task<IResult> ListSubmissionsAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpResultsService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken,
        int page = 1, int pageSize = 25)
    {
        SetPrivateHeaders(context);
        if (page < 1 || pageSize is < 1 or > 100)
            return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [page < 1 ? "page" : "pageSize"] = [page < 1 ? "Page must be at least 1." : "Page size must be between 1 and 100."]
            }));
        return ExecuteResultsAsync(context, accountResolver, rateLimiter, CreatorRsvpRateLimitBucket.Read, cancellationToken,
            accountId => service.ListAsync(accountId, invitationId, page, pageSize, cancellationToken),
            result => result.Outcome == CreatorRsvpResultsOutcome.Succeeded
                ? Results.Ok(result.Page)
                : Results.NotFound());
    }

    private static Task<IResult> GetSubmissionAsync(Guid invitationId, Guid submissionId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpResultsService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteResultsAsync(context, accountResolver, rateLimiter, CreatorRsvpRateLimitBucket.Read, cancellationToken,
            accountId => service.GetAsync(accountId, invitationId, submissionId, cancellationToken),
            result => result.Outcome == CreatorRsvpResultsOutcome.Succeeded
                ? Results.Ok(result.Submission)
                : Results.NotFound());

    private static Task<IResult> DeleteSubmissionAsync(Guid invitationId, Guid submissionId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpResultsService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteResultsAsync(context, accountResolver, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.DeleteAsync(accountId, invitationId, submissionId, cancellationToken),
            result => result.Outcome == CreatorRsvpResultsOutcome.Succeeded
                ? Results.NoContent()
                : Results.NotFound());

    private static Task<IResult> GetAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Read, cancellationToken,
            accountId => service.GetAsync(accountId, invitationId, cancellationToken));

    private static Task<IResult> SetEnabledAsync(Guid invitationId, SetRsvpEnabledRequest request, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.SetEnabledAsync(accountId, invitationId, request, cancellationToken));

    private static Task<IResult> AddQuestionAsync(Guid invitationId, SaveRsvpQuestionRequest request, HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.AddQuestionAsync(accountId, invitationId, request, cancellationToken));

    private static Task<IResult> UpdateQuestionAsync(Guid invitationId, Guid questionId, SaveRsvpQuestionRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.UpdateQuestionAsync(accountId, invitationId, questionId, request, cancellationToken));

    private static Task<IResult> ArchiveQuestionAsync(Guid invitationId, Guid questionId, [FromBody] RsvpRevisionRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.ArchiveQuestionAsync(accountId, invitationId, questionId, request, cancellationToken));

    private static Task<IResult> ReorderQuestionsAsync(Guid invitationId, ReorderRsvpQuestionsRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] ICreatorRsvpConfigurationService service, [FromServices] ICreatorRsvpRateLimiter rateLimiter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(context, accountResolver, service, rateLimiter, CreatorRsvpRateLimitBucket.Write, cancellationToken,
            accountId => service.ReorderQuestionsAsync(accountId, invitationId, request, cancellationToken));

    private static async Task<IResult> ExecuteAsync(HttpContext context, ICurrentAccountResolver resolver,
        ICreatorRsvpConfigurationService service, ICreatorRsvpRateLimiter rateLimiter,
        CreatorRsvpRateLimitBucket bucket, CancellationToken cancellationToken,
        Func<Guid, Task<CreatorRsvpConfigurationResult>> action)
    {
        SetPrivateHeaders(context);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var identityUserId)) return Results.Forbid();
        var accountId = await resolver.ResolveAccountIdAsync(identityUserId, cancellationToken);
        if (accountId is null) return Results.Forbid();
        if (!rateLimiter.TryAcquire(accountId.Value, bucket)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        return ToResult(await action(accountId.Value));
    }

    private static async Task<IResult> ExecuteResultsAsync<T>(HttpContext context, ICurrentAccountResolver resolver,
        ICreatorRsvpRateLimiter rateLimiter, CreatorRsvpRateLimitBucket bucket, CancellationToken cancellationToken,
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

    private static IResult ToResult(CreatorRsvpConfigurationResult result) => result.Outcome switch
    {
        CreatorRsvpConfigurationOutcome.Succeeded => Results.Ok(result.Configuration),
        CreatorRsvpConfigurationOutcome.NotFound => Results.NotFound(),
        CreatorRsvpConfigurationOutcome.Invalid => Results.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()),
        CreatorRsvpConfigurationOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "RSVP configuration was updated elsewhere.",
            extensions: new Dictionary<string, object?> { ["currentRevision"] = result.CurrentRevision }),
        CreatorRsvpConfigurationOutcome.LifecycleConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "RSVP configuration cannot be changed in the current invitation state.",
            extensions: new Dictionary<string, object?> { ["effectiveState"] = result.EffectiveState }),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}
