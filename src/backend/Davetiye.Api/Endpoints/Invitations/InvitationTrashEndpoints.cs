using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Invitations;

public static class InvitationTrashEndpoints
{
    public static IEndpointRouteBuilder MapInvitationTrashEndpoints(this IEndpointRouteBuilder apiV1,
        string readPolicy, string actionPolicy)
    {
        var group = apiV1.MapGroup("/invitations").RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            }).AddEndpointFilter<RequireHttpsInProductionFilter>();
        group.MapGet("/trash", ListAsync).Produces<InvitationTrashPage>().ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden).RequireRateLimiting(readPolicy);
        group.MapPost("/{invitationId:guid}/trash", DeleteAsync).Produces<InvitationTrashItem>()
            .ProducesValidationProblem().ProducesProblem(409).Produces(404).Produces(403)
            .RequireRateLimiting(actionPolicy).AddEndpointFilter<AntiforgeryEndpointFilter>();
        group.MapPost("/{invitationId:guid}/restore", RestoreAsync).Produces<PublicationStatus>()
            .ProducesValidationProblem().ProducesProblem(409).Produces(404).Produces(403)
            .RequireRateLimiting(actionPolicy).AddEndpointFilter<AntiforgeryEndpointFilter>();
        return apiV1;
    }

    private static async Task<IResult> ListAsync(HttpContext context, [FromServices] ICurrentAccountResolver accounts,
        [FromServices] IInvitationTrashService trash, CancellationToken token, int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Invalid pagination."] });
        var account = await AccountAsync(context, accounts, token);
        if (account is null) return Results.Forbid();
        try { return Results.Ok(await trash.ListAsync(account.Value, page, pageSize, token)); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
    }

    private static Task<IResult> DeleteAsync(Guid invitationId, InvitationTrashRequest request, HttpContext context,
        [FromServices] ICurrentAccountResolver accounts, [FromServices] IInvitationTrashService trash,
        CancellationToken token) => ExecuteAsync(invitationId, request, context, accounts, trash, false, token);
    private static Task<IResult> RestoreAsync(Guid invitationId, InvitationTrashRequest request, HttpContext context,
        [FromServices] ICurrentAccountResolver accounts, [FromServices] IInvitationTrashService trash,
        CancellationToken token) => ExecuteAsync(invitationId, request, context, accounts, trash, true, token);

    private static async Task<IResult> ExecuteAsync(Guid id, InvitationTrashRequest request, HttpContext context,
        ICurrentAccountResolver accounts, IInvitationTrashService trash, bool restore, CancellationToken token)
    {
        var account = await AccountAsync(context, accounts, token);
        if (account is null) return Results.Forbid();
        var result = restore ? await trash.RestoreAsync(account.Value, id, request, token)
            : await trash.DeleteAsync(account.Value, id, request, token);
        if (result.Code == "Succeeded") return restore ? Results.Ok(result.Status) : Results.Ok(result.Item);
        if (result.Code == "NotFound") return Results.NotFound();
        if (result.Code == "AccountInactive") return Results.Forbid();
        var extensions = new Dictionary<string, object?> { ["code"] = result.Code, ["currentExpected"] = result.CurrentExpected };
        return result.Code == "InvalidRequest"
            ? Results.ValidationProblem(new Dictionary<string, string[]>(), extensions: extensions)
            : Results.Problem(statusCode: 409, title: "Trash action could not be completed.", extensions: extensions);
    }

    private static async Task<Guid?> AccountAsync(HttpContext context, ICurrentAccountResolver resolver, CancellationToken token) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? await resolver.ResolveAccountIdAsync(userId, token) : null;
}
