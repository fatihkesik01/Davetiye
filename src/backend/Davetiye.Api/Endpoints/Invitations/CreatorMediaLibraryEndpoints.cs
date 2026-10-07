using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

public static class CreatorMediaLibraryEndpoints
{
    public static IEndpointRouteBuilder MapCreatorMediaLibraryEndpoints(this IEndpointRouteBuilder routes, string rateLimitPolicy)
    {
        routes.MapGet("/creator/invitations/{invitationId:guid}/media", ListAsync)
            .RequireAuthorization().RequireRateLimiting(rateLimitPolicy).Produces<CreatorMediaLibraryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        routes.MapPut("/creator/invitations/{invitationId:guid}/media/{assetId:guid}/placement", SetPlacementAsync)
            .RequireAuthorization().AddEndpointFilter<RequireHttpsInProductionFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(rateLimitPolicy).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        routes.MapDelete("/creator/invitations/{invitationId:guid}/media/{assetId:guid}", DeleteAsync)
            .RequireAuthorization().AddEndpointFilter<RequireHttpsInProductionFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(rateLimitPolicy).Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return routes;
    }

    private static async Task<IResult> ListAsync(Guid invitationId, HttpContext context,
        [FromServices] ICurrentAccountResolver accounts, [FromServices] ICreatorMediaLibraryService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accountId = await ResolveAccountAsync(context, accounts, cancellationToken);
        if (accountId is null) return Results.Forbid();
        var result = await service.ListAsync(accountId.Value, invitationId, cancellationToken);
        return result.Outcome == "Succeeded" ? Results.Ok(new CreatorMediaLibraryResponse(result.Assets!)) : Results.NotFound();
    }

    private static async Task<IResult> SetPlacementAsync(Guid invitationId, Guid assetId, SetCreatorMediaPlacementRequest request,
        HttpContext context, [FromServices] ICurrentAccountResolver accounts,
        [FromServices] ICreatorMediaLibraryService service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accountId = await ResolveAccountAsync(context, accounts, cancellationToken);
        if (accountId is null) return Results.Forbid();
        if (!Enum.TryParse<MediaPresentationRole>(request.Role, ignoreCase: false, out var role) ||
            !Enum.IsDefined(role) || request.SortOrder is < 0 or > 1000) return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = ["A valid role and sort order are required."] });
        var result = await service.SetPlacementAsync(new(accountId.Value, invitationId, assetId, role,
            request.SortOrder), cancellationToken);
        return result.Outcome switch
        {
            "Succeeded" => Results.NoContent(),
            "NotFound" => Results.NotFound(),
            "NotReady" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only verified media can be presented."),
            "UnsupportedModule" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The template does not support this presentation."),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["The placement request is invalid."] })
        };
    }

    private static async Task<IResult> DeleteAsync(Guid invitationId, Guid assetId, HttpContext context,
        [FromServices] ICurrentAccountResolver accounts, [FromServices] ICreatorMediaLibraryService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accountId = await ResolveAccountAsync(context, accounts, cancellationToken);
        if (accountId is null) return Results.Forbid();
        var result = await service.DeleteAsync(new(accountId.Value, invitationId, assetId), cancellationToken);
        return result.Outcome switch
        {
            "Deleting" => Results.Accepted(),
            "NotFound" => Results.NotFound(),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["The delete request is invalid."] })
        };
    }

    private static async Task<Guid?> ResolveAccountAsync(HttpContext context, ICurrentAccountResolver accounts,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var identityId)) return null;
        return await accounts.ResolveAccountIdAsync(identityId, cancellationToken);
    }
}

public sealed record SetCreatorMediaPlacementRequest(string Role, int SortOrder);
public sealed record CreatorMediaLibraryResponse(IReadOnlyList<CreatorMediaAssetView> Assets);
