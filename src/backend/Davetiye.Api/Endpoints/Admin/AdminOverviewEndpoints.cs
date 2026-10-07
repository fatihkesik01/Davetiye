using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Api.Infrastructure.Security;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminOverviewEndpoints
{
    public static IEndpointRouteBuilder MapAdminOverviewEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string mfaCompletePolicy,
        string readRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaCompletePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(readRateLimitPolicy);

        apiV1Group.MapGet("/admin/overview", GetOverviewAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(readRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<AdminOverview>();

        return apiV1Group;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext httpContext,
        IAdminOverviewService overviewService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var overview = await overviewService.GetAsync(cancellationToken);
        return Results.Ok(overview);
    }
}
