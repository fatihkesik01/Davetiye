using Davetiye.Application.Modules.Templates.Contracts;

namespace Davetiye.Api.Endpoints.Templates;

/// <summary>Public, PII-free catalog metadata consumed by the later /sablonlar and Creator flows.</summary>
public static class TemplateCatalogEndpoints
{
    public static IEndpointRouteBuilder MapTemplateCatalogEndpoints(this IEndpointRouteBuilder apiV1Group)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);

        apiV1Group.MapGet("/templates", ListActiveAsync)
            .Produces<TemplateCatalogItem[]>();
        return apiV1Group;
    }

    private static async Task<IResult> ListActiveAsync(
        HttpContext httpContext,
        ITemplateCatalogService templateCatalogService,
        CancellationToken cancellationToken)
    {
        // Catalog metadata can change operationally; do not let a browser reuse a stale premium or
        // active-state view. The endpoint intentionally exposes no drafts, ownership IDs or admin data.
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await templateCatalogService.ListActiveAsync(cancellationToken));
    }
}
