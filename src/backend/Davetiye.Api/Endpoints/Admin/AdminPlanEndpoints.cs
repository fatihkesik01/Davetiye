using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminPlanEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlanEndpoints(this IEndpointRouteBuilder routes, string mfaPolicy, string readRateLimit, string writeRateLimit)
    {
        routes.MapGet("/admin/plans", async (HttpContext context, IAdminPlanService service, CancellationToken token) =>
            { SetNoStore(context); return Results.Ok(await service.ListAsync(token)); })
            .RequireAuthorization(mfaPolicy).RequireRateLimiting(readRateLimit).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<IReadOnlyList<AdminPlanItem>>().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);

        routes.MapPut("/admin/plans/{planId:guid}", UpdateAsync)
            .RequireAuthorization(mfaPolicy).RequireRateLimiting(writeRateLimit).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces<AdminPlanItem>().ProducesProblem(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return routes;
    }

    private static async Task<IResult> UpdateAsync(Guid planId, AdminPlanUpdateRequest? request, HttpContext context, IAdminPlanService service, CancellationToken token)
    {
        SetNoStore(context);
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Results.Problem(statusCode: 403, title: "A valid administrator identity is required.");
        if (request is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["A plan update request is required."] });
        var result = await service.UpdateAsync(actorId, planId, request, token);
        return result.Outcome switch
        {
            AdminPlanUpdateOutcome.Succeeded => Results.Ok(result.Plan),
            AdminPlanUpdateOutcome.NotFound => Results.NotFound(),
            AdminPlanUpdateOutcome.Conflict => Results.Problem(statusCode: 409, title: "The plan changed. Refresh and try again."),
            AdminPlanUpdateOutcome.InvalidRequest => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminPlanUpdateRequest.DisplayName)] = ["Name must be 1–200 characters."],
                [nameof(AdminPlanUpdateRequest.Description)] = ["Description cannot exceed 2000 characters."],
                [nameof(AdminPlanUpdateRequest.PriceAmount)] = ["Price must fit numeric(19,4); Free must be zero and paid billing kinds must be positive."],
                [nameof(AdminPlanUpdateRequest.BillingKind)] = ["Billing kind must be Free, OneTime, or Monthly; Free plans must be zero-priced."],
                [nameof(AdminPlanUpdateRequest.Entitlements)] = ["Provide exactly one valid value for each supported entitlement key."],
                [nameof(AdminPlanUpdateRequest.ExpectedRevision)] = ["Expected revision must not be negative."]
            }),
            _ => Results.Problem(statusCode: 500, title: "The plan could not be updated.")
        };
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
