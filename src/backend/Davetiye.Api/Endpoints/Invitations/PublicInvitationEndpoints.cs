using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Invitations.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using System.Security.Claims;
using Davetiye.Application.Modules.Analytics.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Api.Infrastructure.Hosting;

namespace Davetiye.Api.Endpoints.Invitations;

/// <summary>Marks responses that must remain non-cacheable and excluded from search indexing.</summary>
public sealed class PublicInvitationEndpointMetadata;

public static class PublicInvitationEndpoints
{
    public static IEndpointRouteBuilder MapPublicInvitationEndpoints(this IEndpointRouteBuilder apiV1,
        string rateLimitPolicy)
    {
        apiV1.MapGet("/public/invitations/{publicCode}", GetAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata())
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(rateLimitPolicy)
            .Produces<PublicInvitationActive>()
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests)
            .AddOpenApiOperationTransformer(async (operation, context, token) =>
            {
                // Both runtime variants are HTTP 200. Explicit union metadata keeps generated
                // clients honest instead of the last Produces(200) silently replacing the first.
                var active = await context.GetOrCreateSchemaAsync(typeof(PublicInvitationActive), null, token);
                var unavailable = await context.GetOrCreateSchemaAsync(typeof(PublicInvitationUnavailable), null, token);
                operation.Responses!["200"].Content!["application/json"].Schema = new OpenApiSchema
                {
                    AnyOf = [active, unavailable]
                };
            });
        apiV1.MapPost("/public/invitations/{publicCode}/media/{assetId:guid}/delivery", CreateMediaDeliveryAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(rateLimitPolicy).Produces<PublicMediaDeliveryResponse>()
            .Produces(404).Produces(503);
        apiV1.MapPost("/public/invitations/{publicCode}/views", RecordViewAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(rateLimitPolicy).Produces(204).Produces(404);
        apiV1.MapGet("/invitations/{invitationId:guid}/statistics", StatisticsAsync).RequireAuthorization()
            .WithMetadata(new PublicInvitationEndpointMetadata())
            .AddEndpointFilter<RequireHttpsInProductionFilter>().RequireRateLimiting(rateLimitPolicy)
            .Produces<InvitationStatistics>().Produces(403).Produces(404);
        apiV1.MapGet("/public/capabilities", (IConfiguration configuration, PublicInvitationPresentationSettings web, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var key = configuration["PublicMaps:GoogleEmbedApiKey"];
            return Results.Ok(new PublicInvitationCapabilities(web.BaseUrl.TrimEnd('/'), !string.IsNullOrWhiteSpace(key),
                "https://www.google.com", "/maps/embed/v1/place", string.IsNullOrWhiteSpace(key) ? null : key));
        }).AllowAnonymous().RequireRateLimiting(rateLimitPolicy).Produces<PublicInvitationCapabilities>();
        return apiV1;
    }

    private static async Task<IResult> CreateMediaDeliveryAsync(string publicCode, Guid assetId, HttpContext context,
        IPublicMediaDeliveryService delivery, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await delivery.CreateAsync(publicCode, assetId, token);
        return result.Outcome switch
        {
            "Succeeded" => Results.Ok(new PublicMediaDeliveryResponse(result.MediaKind!, result.Url!, result.ExpiresAt!.Value)),
            "Unavailable" => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> GetAsync(string publicCode,
        [FromServices] IPublicInvitationService invitations, CancellationToken token)
    {
        var result = await invitations.GetAsync(publicCode, token);
        return result.Outcome switch
        {
            PublicInvitationOutcome.Active => Results.Ok(result.Invitation),
            PublicInvitationOutcome.Unavailable => Results.Ok(new PublicInvitationUnavailable()),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> RecordViewAsync(string publicCode, HttpContext context,
        IPublicInvitationService invitations, IInvitationViewCounter counter, CancellationToken token)
    {
        // A dedicated render event, never the content GET/poll/HTML crawler route. These signals
        // reduce accidental bot/Creator counts; aggregate views are not unique-visitor analytics.
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (context.User.Identity?.IsAuthenticated == true ||
            context.Request.Headers["X-Invitation-Render"] != "1" ||
            context.Request.Headers["Sec-Fetch-Site"] != "same-origin" ||
            BotUserAgent(userAgent)) return Results.NoContent();
        var result = await invitations.GetAsync(publicCode, token);
        if (result.Outcome == PublicInvitationOutcome.NotFound) return Results.NotFound();
        if (result.Outcome == PublicInvitationOutcome.Active && result.InvitationId is not null)
            await counter.RecordAsync(result.InvitationId.Value, token);
        return Results.NoContent();
    }

    private static bool BotUserAgent(string value) => new[] { "bot", "crawler", "spider", "facebookexternalhit", "WhatsApp", "preview", "slurp" }
        .Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static async Task<IResult> StatisticsAsync(Guid invitationId, HttpContext context,
        ICurrentAccountResolver currentAccount, IAccountReferenceValidator accounts,
        IInvitationOwnershipValidator ownership, IInvitationStatisticsReader statisticsReader, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Forbid();
        var account = await currentAccount.ResolveAccountIdAsync(userId, token);
        if (account is null || await accounts.GetStatusAsync(account.Value, token) != AccountReferenceStatus.Verified)
            return Results.Forbid();
        if (!await ownership.IsOwnedByAccountAsync(account.Value, invitationId, token)) return Results.NotFound();
        var stats = await statisticsReader.ReadAsync(invitationId, token);
        return Results.Ok(new InvitationStatistics(stats.TotalPageViews, stats.RsvpResponseCount,
            stats.ParticipantCountTotal, stats.MemoryCount, stats.ReadyMediaCount,
            stats.ActiveGiftReservationCount));
    }
}

public sealed record PublicMediaDeliveryResponse(string Kind, Uri Url, DateTimeOffset ExpiresAt);
