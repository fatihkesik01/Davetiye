using System.Security.Claims;
using System.Text.Json.Serialization;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Api.Infrastructure.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace Davetiye.Api.Endpoints.Payments;

public static class PaymentCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapPaymentCheckoutEndpoints(this IEndpointRouteBuilder endpoints,
        string writeRateLimitPolicy)
    {
        var group = endpoints.MapGroup("/invitations/{invitationId:guid}/checkout")
            .RequireAuthorization().AddEndpointFilter<RequireHttpsInProductionFilter>();
        group.MapPost("", StartAsync).AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(writeRateLimitPolicy)
            .Produces<PaymentCheckoutResponse>(StatusCodes.Status201Created)
            .Produces<PaymentCheckoutResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapIndividualPurchasePlanEndpoints(this IEndpointRouteBuilder endpoints,
        string readRateLimitPolicy)
    {
        endpoints.MapGet("/payments/plans", async (HttpContext context, ICurrentAccountResolver accountResolver,
            IPaymentCheckoutService checkoutService, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue))
                return Results.Forbid();
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Forbid();
            var accountId = await accountResolver.ResolveAccountIdAsync(userId, cancellationToken);
            if (accountId is null) return Results.Forbid();
            var catalog = await checkoutService.ListPlansAsync(accountId.Value, cancellationToken);
            return catalog.IsEligible ? Results.Ok(catalog.Plans) : Results.Forbid();
        }).RequireAuthorization().AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy)
            .Produces<IReadOnlyList<IndividualPurchasePlan>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
        return endpoints;
    }

    private static async Task<IResult> StartAsync(Guid invitationId, StartPaymentCheckoutBody body,
        HttpContext context, ICurrentAccountResolver accountResolver, IPaymentCheckoutService checkoutService,
        CancellationToken cancellationToken, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue))
            return Results.Forbid();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Forbid();
        var accountId = await accountResolver.ResolveAccountIdAsync(userId, cancellationToken);
        if (accountId is null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Header is required."] });

        var result = await checkoutService.StartAsync(accountId.Value,
            new StartPaymentCheckoutRequest(invitationId, body.PlanKey, idempotencyKey), cancellationToken);
        return result.Outcome switch
        {
            PaymentCheckoutOutcome.Created => Results.Json(result.Checkout, statusCode: StatusCodes.Status201Created),
            PaymentCheckoutOutcome.Replayed => Results.Ok(result.Checkout),
            PaymentCheckoutOutcome.NotFound => Results.NotFound(),
            PaymentCheckoutOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "A payment attempt already exists or the idempotency key was reused with different parameters."),
            PaymentCheckoutOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            PaymentCheckoutOutcome.NotEligible => Results.Forbid(),
            PaymentCheckoutOutcome.ProviderUnavailable => Results.Json(result.Checkout,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Checkout could not be confirmed. Reuse the same Idempotency-Key to check this attempt.")
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartPaymentCheckoutBody(string PlanKey);
