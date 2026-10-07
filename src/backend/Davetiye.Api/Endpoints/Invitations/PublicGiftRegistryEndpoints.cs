using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Domain.Modules.Invitations;

namespace Davetiye.Api.Endpoints.Invitations;

public static class PublicGiftRegistryEndpoints
{
    public static IEndpointRouteBuilder MapPublicGiftRegistryEndpoints(this IEndpointRouteBuilder apiV1, string readRateLimitPolicy,
        string reservationRateLimitPolicy)
    {
        apiV1.MapGet("/public/invitations/{publicCode}/gifts", GetAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicGiftRegistry>().Produces(404).Produces(429);
        apiV1.MapPost("/public/invitations/{publicCode}/gifts/reservations", ReserveAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(reservationRateLimitPolicy).Produces<PublicGiftReservationResponse>(201)
            .ProducesValidationProblem().Produces(403).Produces(404).Produces(429);
        apiV1.MapGet("/public/invitations/{publicCode}/gifts/reservations", ListMyReservationsAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<IReadOnlyList<PublicGiftGuestReservation>>().Produces(404).Produces(429);
        apiV1.MapDelete("/public/invitations/{publicCode}/gifts/reservations/{reservationId:guid}", CancelAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(reservationRateLimitPolicy).Produces(204).Produces(404).Produces(429);
        return apiV1;
    }

    private static async Task<IResult> GetAsync(string publicCode, HttpContext context, IPublicGiftRegistryService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await service.GetAsync(publicCode, cancellationToken);
        return result.Outcome == PublicGiftRegistryOutcome.Available ? Results.Ok(result.Registry) : Results.NotFound();
    }

    private static async Task<IResult> ReserveAsync(string publicCode, ReservePublicGiftRequest request, HttpContext context,
        IPublicGiftRegistryService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.ReserveAsync(publicCode, request, context.Request.Cookies[CookieName(publicCode)], cancellationToken);
        if (result.Outcome == PublicGiftRegistryOutcome.Invalid) return Results.ValidationProblem(result.Errors!);
        if (result.Outcome != PublicGiftRegistryOutcome.Available) return Results.NotFound();
        context.Response.Cookies.Append(CookieName(publicCode), result.SessionToken!, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/",
            Expires = result.WindowEndsAt, IsEssential = true
        });
        return Results.Created(string.Empty, new PublicGiftReservationResponse(result.ReservationId!.Value));
    }

    private static async Task<IResult> CancelAsync(string publicCode, Guid reservationId, HttpContext context,
        IPublicGiftRegistryService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        return await service.CancelAsync(publicCode, reservationId, context.Request.Cookies[CookieName(publicCode)], cancellationToken) == PublicGiftRegistryOutcome.Available
            ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ListMyReservationsAsync(string publicCode, HttpContext context,
        IPublicGiftRegistryService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.ListMyReservationsAsync(publicCode, context.Request.Cookies[CookieName(publicCode)], cancellationToken);
        return result.Outcome == PublicGiftRegistryOutcome.Available ? Results.Ok(result.Reservations) : Results.NotFound();
    }

    private static string CookieName(string publicCode) => PublicInvitationCode.IsValid(publicCode)
        ? $"__Host-davetiye-gift-{publicCode}" : "__Host-davetiye-gift-invalid";

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed record PublicGiftReservationResponse(Guid ReservationId);
