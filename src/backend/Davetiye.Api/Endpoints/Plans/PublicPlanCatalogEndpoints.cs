using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

namespace Davetiye.Api.Endpoints.Plans;

/// <summary>
/// Anonymous, read-only plan cards for the public landing page (PRODUCT §30a item 5). Prices and limits
/// come from the DB-managed catalog; the response carries no IDs, grants, revisions or provider data.
/// </summary>
public static class PublicPlanCatalogEndpoints
{
    public const string Route = "/public/plans";

    public static IEndpointRouteBuilder MapPublicPlanCatalogEndpoints(this IEndpointRouteBuilder apiV1,
        string readRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        apiV1.MapGet(Route, ListActiveAsync).AllowAnonymous()
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy)
            .Produces<PublicPlanCatalogItem[]>()
            .Produces(StatusCodes.Status429TooManyRequests);
        return apiV1;
    }

    private static async Task<IResult> ListActiveAsync(HttpContext context, IPublicPlanCatalogReader reader,
        CancellationToken cancellationToken)
    {
        // Never cacheable: a request carrying the auth cookie can receive a sliding-renewal Set-Cookie
        // from the cookie handler, and the SPA fetches this with no-store anyway.
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await reader.ListActiveAsync(cancellationToken));
    }
}
