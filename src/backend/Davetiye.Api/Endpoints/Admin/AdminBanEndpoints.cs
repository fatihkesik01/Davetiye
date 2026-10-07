using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminBanEndpoints
{
    public static IEndpointRouteBuilder MapAdminBanEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string mfaCompletePolicy,
        string actionRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaCompletePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionRateLimitPolicy);

        apiV1Group.MapPost("/admin/accounts/{accountId:guid}/ban", BanAccountAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(actionRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Accepts<AdminBanRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        apiV1Group.MapPost("/admin/accounts/{accountId:guid}/unban", UnbanAccountAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(actionRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return apiV1Group;
    }

    private static async Task<IResult> BanAccountAsync(
        Guid accountId,
        AdminBanRequest? request,
        HttpContext httpContext,
        IAdminBanService banService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A valid administrator identity is required.");
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminBanRequest.Reason)] = ["A reason is required and must be no longer than 2000 characters."]
            });
        }

        var result = await banService.BanAsync(actorId, accountId, request, cancellationToken);
        return result.Outcome switch
        {
            AdminBanOutcome.Succeeded => Results.NoContent(),
            AdminBanOutcome.AccountNotFound => Results.NotFound(),
            AdminBanOutcome.AlreadyBanned => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This account is already banned."),
            AdminBanOutcome.InvalidRequest => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminBanRequest.Reason)] = ["A reason is required and must be no longer than 2000 characters."],
                [nameof(AdminBanRequest.InternalNote)] = ["The internal note must be no longer than 2000 characters."]
            }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The account ban could not be completed.")
        };
    }

    private static async Task<IResult> UnbanAccountAsync(
        Guid accountId,
        HttpContext httpContext,
        IAdminBanService banService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A valid administrator identity is required.");
        }

        var result = await banService.UnbanAsync(actorId, accountId, cancellationToken);
        return result.Outcome switch
        {
            AdminUnbanOutcome.Succeeded => Results.NoContent(),
            AdminUnbanOutcome.AccountNotFound => Results.NotFound(),
            AdminUnbanOutcome.NoActiveBan => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This account has no active ban."),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The account unban could not be completed.")
        };
    }
}
