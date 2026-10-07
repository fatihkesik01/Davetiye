using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints;

public static class AccountDeletionEndpoints
{
    public static IEndpointRouteBuilder MapAccountDeletionEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string requestRateLimitPolicy,
        string confirmRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmRateLimitPolicy);

        var group = apiV1Group.MapGroup("/account/deletion-requests");
        group.MapPost("", RequestAsync)
            .RequireAuthorization()
            .RequireRateLimiting(requestRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/confirm", ConfirmAsync)
            .AllowAnonymous()
            .RequireRateLimiting(confirmRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Accepts<ConfirmAccountDeletionRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return apiV1Group;
    }

    private static async Task<IResult> RequestAsync(
        HttpContext httpContext,
        ICurrentAccountResolver currentAccountResolver,
        IAccountDeletionService accountDeletionService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var identityUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(identityUserId, out var userId))
            return Results.Unauthorized();

        var accountId = await currentAccountResolver.ResolveAccountIdAsync(userId, cancellationToken);
        if (accountId is null)
            return Results.Forbid();

        var outcome = await accountDeletionService.RequestAsync(accountId.Value, cancellationToken);
        return outcome switch
        {
            AccountDeletionRequestOutcome.Accepted => Results.Accepted(),
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This account is unavailable for a new deletion request."),
        };
    }

    private static async Task<IResult> ConfirmAsync(
        ConfirmAccountDeletionRequest? request,
        HttpContext httpContext,
        IAccountDeletionService accountDeletionService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (request is null || string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 128)
            return InvalidToken();

        var outcome = await accountDeletionService.ConfirmAsync(request.Token, cancellationToken);
        if (outcome != AccountDeletionConfirmationOutcome.Confirmed)
            return InvalidToken();

        // The deletion has already changed durable account state and security stamp. Clear this
        // browser's cookie as well; all other stale cookies are rejected by per-request validation.
        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.NoContent();
    }

    private static IResult InvalidToken() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The account deletion link is invalid or has expired.");
}
