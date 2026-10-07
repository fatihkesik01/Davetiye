using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Davetiye.Api.Endpoints.Invitations;

public static class CreatorGiftRegistryEndpoints
{
    public static IEndpointRouteBuilder MapCreatorGiftRegistryEndpoints(this IEndpointRouteBuilder endpoints,
        string readRateLimitPolicy, string writeRateLimitPolicy)
    {
        var group = endpoints.MapGroup("/invitations/{invitationId:guid}/gifts")
            .RequireAuthorization().AddEndpointFilter<RequireHttpsInProductionFilter>();
        group.MapGet("", ListAsync).RequireRateLimiting(readRateLimitPolicy).Produces<IReadOnlyList<CreatorGiftItem>>();
        group.MapPost("", CreateAsync).AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(writeRateLimitPolicy)
            .Produces<CreatorGiftItem>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
        group.MapPut("/{itemId:guid}", UpdateAsync).AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(writeRateLimitPolicy)
            .Produces<CreatorGiftItem>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status404NotFound);
        group.MapDelete("/{itemId:guid}", DeleteAsync).AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(writeRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict).Produces(StatusCodes.Status404NotFound);
        group.MapPut("/order", ReorderAsync).AddEndpointFilter<AntiforgeryEndpointFilter>().RequireRateLimiting(writeRateLimitPolicy)
            .Produces<IReadOnlyList<CreatorGiftItem>>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/reservations", ListReservationsAsync).RequireRateLimiting(readRateLimitPolicy).Produces<IReadOnlyList<CreatorGiftReservation>>();
        group.MapDelete("/reservations/{reservationId:guid}", RemoveReservationAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy).Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static Task<IResult> ListAsync(Guid invitationId, HttpContext context, ICurrentAccountResolver resolver,
        ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId => ToItems(await service.ListAsync(accountId, invitationId, cancellationToken)));

    private static Task<IResult> CreateAsync(Guid invitationId, CreateGiftItemRequest request, HttpContext context,
        ICurrentAccountResolver resolver, ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId =>
        {
            var result = await service.CreateAsync(accountId, invitationId, request, cancellationToken);
            return result.Outcome switch
            {
                CreatorGiftRegistryOutcome.Succeeded => Results.Created($"/api/v1/invitations/{invitationId}/gifts/{result.Item!.Id}", result.Item),
                CreatorGiftRegistryOutcome.Invalid => Results.ValidationProblem(result.Errors!),
                _ => Results.NotFound()
            };
        });

    private static Task<IResult> UpdateAsync(Guid invitationId, Guid itemId, UpdateGiftItemRequest request, HttpContext context,
        ICurrentAccountResolver resolver, ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId => ToItem(await service.UpdateAsync(accountId, invitationId, itemId, request, cancellationToken)));

    private static Task<IResult> DeleteAsync(Guid invitationId, Guid itemId, HttpContext context, ICurrentAccountResolver resolver,
        ICreatorGiftRegistryService service, CancellationToken cancellationToken, long expectedRevision) =>
        ExecuteAsync(context, resolver, async accountId => ToDelete(await service.DeleteAsync(accountId, invitationId, itemId, expectedRevision, cancellationToken)));

    private static Task<IResult> ReorderAsync(Guid invitationId, ReorderGiftItemsRequest request, HttpContext context,
        ICurrentAccountResolver resolver, ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId => ToItems(await service.ReorderAsync(accountId, invitationId, request, cancellationToken)));

    private static Task<IResult> ListReservationsAsync(Guid invitationId, HttpContext context, ICurrentAccountResolver resolver,
        ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId =>
        {
            var result = await service.ListReservationsAsync(accountId, invitationId, cancellationToken);
            return result.Outcome == CreatorGiftRegistryOutcome.Succeeded ? Results.Ok(result.Reservations) : Results.NotFound();
        });

    private static Task<IResult> RemoveReservationAsync(Guid invitationId, Guid reservationId, HttpContext context,
        ICurrentAccountResolver resolver, ICreatorGiftRegistryService service, CancellationToken cancellationToken) =>
        ExecuteAsync(context, resolver, async accountId =>
            await service.RemoveReservationAsync(accountId, invitationId, reservationId, cancellationToken) == CreatorGiftRegistryOutcome.Succeeded
                ? Results.NoContent() : Results.NotFound());

    private static async Task<IResult> ExecuteAsync(HttpContext context, ICurrentAccountResolver resolver,
        Func<Guid, Task<IResult>> operation)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue)) return Results.Forbid();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Forbid();
        var accountId = await resolver.ResolveAccountIdAsync(userId, context.RequestAborted);
        return accountId is null ? Results.Forbid() : await operation(accountId.Value);
    }

    private static IResult ToItems(CreatorGiftItemsResult result) => result.Outcome switch
    {
        CreatorGiftRegistryOutcome.Succeeded => Results.Ok(result.Items),
        CreatorGiftRegistryOutcome.Invalid => Results.ValidationProblem(result.Errors!),
        _ => Results.NotFound()
    };
    private static IResult ToItem(CreatorGiftItemResult result) => result.Outcome switch
    {
        CreatorGiftRegistryOutcome.Succeeded => Results.Ok(result.Item),
        CreatorGiftRegistryOutcome.Invalid => Results.ValidationProblem(result.Errors!),
        CreatorGiftRegistryOutcome.Conflict => Results.Problem(statusCode: 409, title: "Gift item changed or has active reservations.",
            extensions: result.CurrentRevision is null ? null : new Dictionary<string, object?> { ["currentRevision"] = result.CurrentRevision }),
        _ => Results.NotFound()
    };
    private static IResult ToDelete(CreatorGiftItemResult result) => result.Outcome switch
    {
        CreatorGiftRegistryOutcome.Succeeded => Results.NoContent(),
        CreatorGiftRegistryOutcome.Conflict => Results.Problem(statusCode: 409, title: "Gift item changed or has active reservations."),
        CreatorGiftRegistryOutcome.Invalid => Results.ValidationProblem(result.Errors!),
        _ => Results.NotFound()
    };
}
