using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints;

public static class AccountConsentEndpoints
{
    public static IEndpointRouteBuilder MapAccountConsentEndpoints(this IEndpointRouteBuilder apiV1Group)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);

        var group = apiV1Group.MapGroup("/account/consents").RequireAuthorization();
        group.MapGet("", GetAsync)
            .Produces<AccountConsentSnapshot>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/service-notice", AcknowledgeServiceNoticeAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces<AccountConsentSnapshot>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("/marketing", UpdateMarketingAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces<AccountConsentSnapshot>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        return apiV1Group;
    }

    private static async Task<IResult> AcknowledgeServiceNoticeAsync(
        ServiceNoticeAcknowledgementHttpRequest request,
        HttpContext httpContext,
        [FromServices] IAccountConsentService consentService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (request.Acknowledged is not true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Explicit service notice acknowledgement is required.");
        }

        if (!TryGetIdentityUserId(httpContext, out var identityUserId))
            return Results.Unauthorized();

        var snapshot = await consentService.AcknowledgeServiceNoticeAsync(identityUserId, true, cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        [FromServices] IAccountConsentService consentService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (!TryGetIdentityUserId(httpContext, out var identityUserId))
            return Results.Unauthorized();

        var snapshot = await consentService.GetAsync(identityUserId, cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
    }

    private static async Task<IResult> UpdateMarketingAsync(
        MarketingPreferenceHttpRequest request,
        HttpContext httpContext,
        [FromServices] IAccountConsentService consentService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (request.OptedIn is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The optedIn field is required.");
        }

        if (!TryGetIdentityUserId(httpContext, out var identityUserId))
            return Results.Unauthorized();

        var snapshot = await consentService.UpdateMarketingPreferenceAsync(
            identityUserId, request.OptedIn.Value, cancellationToken);
        return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
    }

    private static bool TryGetIdentityUserId(HttpContext httpContext, out Guid identityUserId) =>
        Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out identityUserId) &&
        identityUserId != Guid.Empty;

    public sealed record MarketingPreferenceHttpRequest(bool? OptedIn);
    public sealed record ServiceNoticeAcknowledgementHttpRequest(bool? Acknowledged);
}
