using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Templates.Contracts;

namespace Davetiye.Api.Endpoints.Admin;

public static class AdminTemplateEndpoints
{
    public static IEndpointRouteBuilder MapAdminTemplateEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string mfaCompletePolicy,
        string readRateLimitPolicy,
        string writeRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaCompletePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(readRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(writeRateLimitPolicy);

        apiV1Group.MapGet("/admin/templates", ListAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(readRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .Produces<IReadOnlyList<AdminTemplateItem>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        apiV1Group.MapPut("/admin/templates/{templateId:guid}", UpdateAsync)
            .RequireAuthorization(mfaCompletePolicy)
            .RequireRateLimiting(writeRateLimitPolicy)
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces<AdminTemplateItem>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return apiV1Group;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        IAdminTemplateService service,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);
        return Results.Ok(await service.ListAsync(cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid templateId,
        AdminTemplateUpdateRequest? request,
        HttpContext httpContext,
        IAdminTemplateService service,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);
        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A valid administrator identity is required.");
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A template update request is required."]
            });
        }

        var result = await service.UpdateAsync(actorId, templateId, request, cancellationToken);
        return result.Outcome switch
        {
            AdminTemplateUpdateOutcome.Succeeded => Results.Ok(result.Template),
            AdminTemplateUpdateOutcome.NotFound => Results.NotFound(),
            AdminTemplateUpdateOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The template changed. Refresh and try again."),
            AdminTemplateUpdateOutcome.InvalidRequest => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminTemplateUpdateRequest.Name)] = ["Name is required and cannot exceed 200 characters."],
                [nameof(AdminTemplateUpdateRequest.Description)] = ["Description cannot exceed 2000 characters."],
                [nameof(AdminTemplateUpdateRequest.ExpectedRevision)] = ["Expected revision must not be negative."]
            }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The template could not be updated.")
        };
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
