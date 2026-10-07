using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Administration.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminSystemSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSystemSettingsEndpoints(
        this IEndpointRouteBuilder routes,
        string mfaPolicy,
        string readRateLimit,
        string writeRateLimit)
    {
        routes.MapGet("/admin/settings", ListAsync)
            .RequireAuthorization(mfaPolicy)
            .RequireRateLimiting(readRateLimit)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<AdminSystemSettingsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        routes.MapPut("/admin/settings/{key}", UpdateAsync)
            .RequireAuthorization(mfaPolicy)
            .RequireRateLimiting(writeRateLimit)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Accepts<AdminSystemSettingUpdateRequest>("application/json")
            .Produces<AdminSystemSettingItem>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IAdminSystemSettingsService service,
        CancellationToken cancellationToken)
    {
        SetNoStore(context);
        return Results.Ok(await service.ListAsync(cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        AdminSystemSettingUpdateRequest? request,
        HttpContext context,
        IAdminSystemSettingsService service,
        CancellationToken cancellationToken)
    {
        SetNoStore(context);
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A valid administrator identity is required.");
        if (request is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["A settings update request is required."] });

        var result = await service.UpdateAsync(actorId, key, request, cancellationToken);
        return result.Outcome switch
        {
            AdminSystemSettingUpdateOutcome.Succeeded => Results.Ok(result.Setting),
            AdminSystemSettingUpdateOutcome.NotFound => Results.NotFound(),
            AdminSystemSettingUpdateOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The setting changed. Refresh and try again."),
            AdminSystemSettingUpdateOutcome.InvalidRequest => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(AdminSystemSettingUpdateRequest.Value)] = ["Value must be an integer from 0 through 365."],
                    [nameof(AdminSystemSettingUpdateRequest.ExpectedRevision)] = ["Expected revision must not be negative."]
                }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The setting could not be updated.")
        };
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
