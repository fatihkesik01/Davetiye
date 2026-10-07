using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

public static class PublicationEndpoints
{
    public static IEndpointRouteBuilder MapPublicationEndpoints(this IEndpointRouteBuilder apiV1,
        string readRateLimitPolicy, string actionRateLimitPolicy)
    {
        var group = apiV1.MapGroup("/invitations/{invitationId:guid}/publication")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            })
            .AddEndpointFilter<RequireHttpsInProductionFilter>();
        group.MapGet("", GetAsync).Produces<PublicationStatus>().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden).RequireRateLimiting(readRateLimitPolicy);
        group.MapPost("/actions", ExecuteAsync).Produces<PublicationStatus>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden).RequireRateLimiting(actionRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();
        return apiV1;
    }

    private static async Task<IResult> GetAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accounts,
        [FromServices] IPublicationLifecycleService publications, CancellationToken token)
    {
        var accountId = await ResolveAccountAsync(context, accounts, token);
        return accountId is null ? Results.Forbid()
            : ToResult(await publications.GetAsync(accountId.Value, invitationId, token));
    }

    private static async Task<IResult> ExecuteAsync(Guid invitationId, PublicationActionRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accounts,
        [FromServices] IPublicationLifecycleService publications, CancellationToken token)
    {
        var accountId = await ResolveAccountAsync(context, accounts, token);
        return accountId is null ? Results.Forbid()
            : ToResult(await publications.ExecuteAsync(accountId.Value, invitationId, request, token));
    }

    private static async Task<Guid?> ResolveAccountAsync(HttpContext context,
        ICurrentAccountResolver resolver, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        var claim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var userId)
            ? await resolver.ResolveAccountIdAsync(userId, token) : null;
    }

    private static IResult ToResult(PublicationLifecycleResult result)
    {
        if (result.Code == "Succeeded") return Results.Ok(result.Status);
        if (result.Code == "NotFound") return Results.NotFound();
        if (result.Code == "AccountInactive") return Results.Forbid();
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = result.Code,
            ["missingRequiredFields"] = result.MissingRequiredFields,
            ["missingRecommendedFields"] = result.MissingRecommendedFields,
            ["currentExpected"] = result.CurrentExpected
        };
        if (result.Code == "InvalidRequest")
            return Results.ValidationProblem(result.Errors?.ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? new Dictionary<string, string[]>(), extensions: extensions);
        if (result.Errors is not null) extensions["errors"] = result.Errors;
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Publication action could not be completed.", extensions: extensions);
    }
}
