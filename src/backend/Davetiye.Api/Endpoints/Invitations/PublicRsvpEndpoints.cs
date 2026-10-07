using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Rsvp.Contracts;

namespace Davetiye.Api.Endpoints.Invitations;

public static class PublicRsvpEndpoints
{
    public static IEndpointRouteBuilder MapPublicRsvpEndpoints(this IEndpointRouteBuilder apiV1,
        string readRateLimitPolicy, string submitRateLimitPolicy)
    {
        apiV1.MapGet("/public/invitations/{publicCode}/rsvp", GetConfigurationAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicRsvpConfiguration>().Produces(404).Produces(429);
        apiV1.MapPost("/public/invitations/{publicCode}/rsvp/submissions", SubmitAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(submitRateLimitPolicy).Produces<PublicRsvpSubmissionResponse>(201)
            .Produces(400).Produces(403).Produces(404).Produces(409).Produces(429);
        apiV1.MapGet("/public/invitations/{publicCode}/rsvp/submissions/{submissionId:guid}", GetSubmissionAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .RequireRateLimiting(readRateLimitPolicy).Produces<PublicRsvpGuestSubmission>().Produces(404).Produces(429);
        apiV1.MapPut("/public/invitations/{publicCode}/rsvp/submissions/{submissionId:guid}", UpdateAsync).AllowAnonymous()
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter<StrictOriginEndpointFilter>().AddEndpointFilter<AntiforgeryEndpointFilter>()
            .RequireRateLimiting(submitRateLimitPolicy).Produces<PublicRsvpUpdateResponse>(200)
            .Produces(400).Produces(403).Produces(404).Produces(429);
        return apiV1;
    }

    private static async Task<IResult> GetConfigurationAsync(string publicCode, HttpContext context,
        IPublicRsvpService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.GetConfigurationAsync(publicCode, cancellationToken);
        return result.Outcome switch
        {
            PublicRsvpOutcome.Available => Results.Ok(result.Configuration),
            PublicRsvpOutcome.NotFound => Results.NotFound(),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> SubmitAsync(string publicCode, SubmitPublicRsvpRequest request,
        HttpContext context, IPublicRsvpService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.SubmitAsync(publicCode, request, cancellationToken);
        return result.Outcome switch
        {
            PublicRsvpOutcome.Available => SetManageCookie(context, result, created: true),
            PublicRsvpOutcome.QuotaReached => Results.Conflict(new { code = "rsvp_quota_reached" }),
            PublicRsvpOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            PublicRsvpOutcome.NotFound or PublicRsvpOutcome.Unavailable => Results.NotFound(),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> GetSubmissionAsync(string publicCode, Guid submissionId,
        HttpContext context, IPublicRsvpService service, CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.GetSubmissionAsync(publicCode, submissionId,
            context.Request.Cookies["__Host-davetiye-rsvp"], cancellationToken);
        return result.Outcome == PublicRsvpOutcome.Available
            ? Results.Ok(result.Submission)
            : Results.NotFound();
    }

    private static async Task<IResult> UpdateAsync(string publicCode, Guid submissionId,
        SubmitPublicRsvpRequest request, HttpContext context, IPublicRsvpService service,
        CancellationToken cancellationToken)
    {
        SetPrivateHeaders(context);
        var result = await service.UpdateAsync(publicCode, submissionId,
            context.Request.Cookies["__Host-davetiye-rsvp"], request, cancellationToken);
        return result.Outcome switch
        {
            PublicRsvpOutcome.Available => SetManageCookie(context, result, created: false),
            PublicRsvpOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            PublicRsvpOutcome.NotFound or PublicRsvpOutcome.Unavailable => Results.NotFound(),
            _ => Results.NotFound()
        };
    }

    private static IResult SetManageCookie(HttpContext context, PublicRsvpSubmissionResult result, bool created)
    {
        // The raw 256-bit token is deliberately delivered only in this HttpOnly cookie, never JSON.
        context.Response.Cookies.Append("__Host-davetiye-rsvp", result.ManageToken!, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = result.CapabilityExpiresAt,
            IsEssential = true
        });
        if (created)
            return Results.Created(string.Empty, new PublicRsvpSubmissionResponse(result.SubmissionId!.Value,
                result.SubmittedAt!.Value));
        return Results.Ok(new PublicRsvpUpdateResponse(result.SubmissionId!.Value, result.UpdatedAt!.Value));
    }

    private static void SetPrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed record PublicRsvpSubmissionResponse(Guid SubmissionId, DateTimeOffset SubmittedAt);
public sealed record PublicRsvpUpdateResponse(Guid SubmissionId, DateTimeOffset UpdatedAt);
