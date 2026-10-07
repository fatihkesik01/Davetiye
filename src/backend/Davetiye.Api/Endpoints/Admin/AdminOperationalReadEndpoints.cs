using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Api.Infrastructure.Security;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminOperationalReadEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAdminOperationalReadEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string mfaCompletePolicy,
        string readRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaCompletePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(readRateLimitPolicy);

        apiV1Group.MapGet("/admin/payments", GetPaymentsAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(readRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<AdminPaymentPage>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        apiV1Group.MapGet("/admin/audit", GetAuditAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(readRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<AdminAuditPage>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return apiV1Group;
    }

    private static async Task<IResult> GetPaymentsAsync(
        int? page,
        int? pageSize,
        HttpContext httpContext,
        IAdminPaymentListReader reader,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);
        if (!TryGetPage(page, pageSize, out var requestedPage, out var requestedPageSize))
            return InvalidPage();

        return Results.Ok(await reader.GetPageAsync(requestedPage, requestedPageSize, cancellationToken));
    }

    private static async Task<IResult> GetAuditAsync(
        int? page,
        int? pageSize,
        HttpContext httpContext,
        IAdminAuditListReader reader,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);
        if (!TryGetPage(page, pageSize, out var requestedPage, out var requestedPageSize))
            return InvalidPage();

        return Results.Ok(await reader.GetPageAsync(requestedPage, requestedPageSize, cancellationToken));
    }

    private static bool TryGetPage(int? page, int? pageSize, out int resolvedPage, out int resolvedPageSize)
    {
        resolvedPage = page ?? 1;
        resolvedPageSize = pageSize ?? DefaultPageSize;
        return resolvedPage >= 1
            && resolvedPageSize is >= 1 and <= MaxPageSize
            && resolvedPage <= int.MaxValue / resolvedPageSize;
    }

    private static IResult InvalidPage() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["page"] = ["Page must be positive and the page offset must be supported."],
        ["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."]
    });

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
