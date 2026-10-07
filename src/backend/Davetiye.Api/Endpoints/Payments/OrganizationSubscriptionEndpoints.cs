using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Davetiye.Api.Endpoints.Payments;

public static class OrganizationSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationSubscriptionEndpoints(
        this IEndpointRouteBuilder endpoints,
        string readRateLimitPolicy,
        string writeRateLimitPolicy)
    {
        var group = endpoints.MapGroup("/payments/organization-subscription")
            .RequireAuthorization()
            .AddEndpointFilter<PrivateResponseHeadersFilter>()
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        group.MapGet("", GetAsync)
            .RequireRateLimiting(readRateLimitPolicy)
            .Produces<OrganizationSubscriptionSnapshotResponse?>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(async (operation, context, token) =>
            {
                _ = await context.GetOrCreateSchemaAsync(
                    typeof(OrganizationSubscriptionSnapshotResponse), null, token);
                operation.Responses!["200"].Content!["application/json"].Schema = new OpenApiSchema
                {
                    AnyOf = [
                        new OpenApiSchemaReference("OrganizationSubscriptionSnapshotResponse", context.Document),
                        new OpenApiSchema { Type = JsonSchemaType.Null },
                    ],
                };
            });

        group.MapPost("/{subscriptionId:guid}/cancel", CancelAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy)
            .Produces<OrganizationSubscriptionCancellationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] IOrganizationSubscriptionLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var accountId = await ResolveAccountAsync(context, accountResolver, cancellationToken);
        if (accountId is null) return Results.Forbid();

        var snapshot = await lifecycle.GetAccessSnapshotAsync(accountId.Value, cancellationToken);
        return snapshot is null
            ? Results.Content("null", "application/json", statusCode: StatusCodes.Status200OK)
            : Results.Json(OrganizationSubscriptionSnapshotResponse.From(snapshot), statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> CancelAsync(
        Guid subscriptionId,
        HttpContext context,
        [FromServices] ICurrentAccountResolver accountResolver,
        [FromServices] IOrganizationSubscriptionLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var accountId = await ResolveAccountAsync(context, accountResolver, cancellationToken);
        if (accountId is null) return Results.Forbid();

        var result = await lifecycle.CancelAtPeriodEndAsync(accountId.Value, subscriptionId, cancellationToken);
        return result.Outcome switch
        {
            OrganizationSubscriptionCommandOutcome.Applied or OrganizationSubscriptionCommandOutcome.Duplicate =>
                Results.Ok(new OrganizationSubscriptionCancellationResponse(
                    result.Outcome.ToString(), result.PaidThroughAtUtc)),
            OrganizationSubscriptionCommandOutcome.NotFound => Results.NotFound(),
            OrganizationSubscriptionCommandOutcome.Conflict or OrganizationSubscriptionCommandOutcome.Stale =>
                Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription can no longer be canceled."),
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription cancellation could not be applied.")
        };
    }

    private static async Task<Guid?> ResolveAccountAsync(
        HttpContext context,
        ICurrentAccountResolver accountResolver,
        CancellationToken cancellationToken)
    {
        if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue))
            return null;
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;
        return await accountResolver.ResolveAccountIdAsync(userId, cancellationToken);
    }

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}

public sealed record OrganizationSubscriptionCancellationResponse(string Outcome, DateTimeOffset? PaidThroughAtUtc);

public readonly record struct OrganizationSubscriptionSnapshotResponse(
    Guid SubscriptionId,
    string Status,
    DateTimeOffset PaidThroughAtUtc,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelRequestedAtUtc,
    string PlanDisplayName,
    decimal PriceAmount,
    string Currency,
    string BillingPeriod)
{
    public static OrganizationSubscriptionSnapshotResponse From(OrganizationSubscriptionAccessSnapshot snapshot) => new(
        snapshot.SubscriptionId,
        snapshot.Status,
        snapshot.PaidThroughAtUtc,
        snapshot.CancelAtPeriodEnd,
        snapshot.CancelRequestedAtUtc,
        snapshot.PlanDisplayName,
        snapshot.PriceAmount,
        snapshot.Currency,
        snapshot.BillingPeriod);
}

internal sealed class PrivateResponseHeadersFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return next(context);
    }
}
