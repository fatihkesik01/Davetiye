using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminBannedAccountEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxEmailPrefixLength = 256;

    public static IEndpointRouteBuilder MapAdminBannedAccountEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string mfaCompletePolicy,
        string readRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaCompletePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(readRateLimitPolicy);

        apiV1Group.MapPost("/admin/accounts/banned/search", SearchBannedAccountsAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(readRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Accepts<AdminBannedAccountSearchRequest>("application/json")
            .Produces<AdminBannedAccountPage>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return apiV1Group;
    }

    private static async Task<IResult> SearchBannedAccountsAsync(
        AdminBannedAccountSearchRequest request,
        HttpContext httpContext,
        IAdminBannedAccountListReader reader,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var requestedPage = request.Page ?? 1;
        var requestedPageSize = request.PageSize ?? DefaultPageSize;
        if (requestedPage < 1
            || requestedPageSize is < 1 or > MaxPageSize
            || requestedPage > int.MaxValue / requestedPageSize
            || request.EmailPrefix?.Length > MaxEmailPrefixLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["page"] = ["Page must be positive and the page offset must be supported."],
                ["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."],
                ["emailPrefix"] = [$"Email prefix cannot exceed {MaxEmailPrefixLength} characters."]
            });
        }

        return Results.Ok(await reader.GetPageAsync(
            requestedPage,
            requestedPageSize,
            request.EmailPrefix,
            cancellationToken));
    }
}
