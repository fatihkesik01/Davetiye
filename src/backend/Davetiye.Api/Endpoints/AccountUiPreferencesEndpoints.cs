using System.Security.Claims;
using System.Text.Json;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints;

public static class AccountUiPreferencesEndpoints
{
    private static readonly HashSet<string> Locales = ["tr", "en"];
    private static readonly HashSet<string> ColorThemes = ["kutlio", "sage", "rose", "ocean", "plum"];
    private static readonly HashSet<string> Appearances = ["system", "light", "dark"];
    private static readonly HashSet<string> Avatars =
        ["sunny", "mint", "berry", "sky", "coral", "lilac", "amber", "forest", "night", "rose", "slate", "peach"];

    public static IEndpointRouteBuilder MapAccountUiPreferencesEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string readRateLimitPolicy,
        string writeRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(readRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(writeRateLimitPolicy);

        var group = apiV1Group.MapGroup("/account/preferences")
            .RequireAuthorization()
            .AddEndpointFilter<RequireHttpsInProductionFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        group.MapGet("", GetAsync)
            .RequireRateLimiting(readRateLimitPolicy)
            .Produces<AccountUiPreferences>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("", UpdateAsync)
            .RequireRateLimiting(writeRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .Produces<AccountUiPreferences>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        return apiV1Group;
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        [FromServices] IAccountUiPreferencesService preferencesService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (!TryGetIdentityUserId(httpContext, out var identityUserId))
            return Results.Unauthorized();

        var preferences = await preferencesService.GetAsync(identityUserId, cancellationToken);
        return preferences is null ? Results.NotFound() : Results.Ok(preferences);
    }

    private static async Task<IResult> UpdateAsync(
        UpdateAccountUiPreferencesRequest request,
        HttpContext httpContext,
        [FromServices] IAccountUiPreferencesService preferencesService,
        [FromServices] IAccountUiPreferencesRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        // Avatar is required-but-nullable: an absent property (Undefined) is rejected, an explicit
        // null clears the choice, and a string must be one of the allow-listed keys.
        var avatarValid = request.Avatar.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => Avatars.Contains(request.Avatar.GetString()!),
            _ => false,
        };
        if (!avatarValid ||
            request.Locale is null || !Locales.Contains(request.Locale) ||
            request.ColorTheme is null || !ColorThemes.Contains(request.ColorTheme) ||
            request.Appearance is null || !Appearances.Contains(request.Appearance))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid account UI preferences.",
                detail: "locale, colorTheme, appearance, and avatar must use supported values.");
        }

        if (!TryGetIdentityUserId(httpContext, out var identityUserId))
            return Results.Unauthorized();

        if (!rateLimiter.TryAcquire(identityUserId))
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        var preferences = await preferencesService.UpdateAsync(
            identityUserId,
            new AccountUiPreferences(request.Locale, request.ColorTheme, request.Appearance,
                request.Avatar.ValueKind == JsonValueKind.String ? request.Avatar.GetString() : null),
            cancellationToken);
        return preferences is null ? Results.NotFound() : Results.Ok(preferences);
    }

    private static bool TryGetIdentityUserId(HttpContext httpContext, out Guid identityUserId) =>
        Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out identityUserId) &&
        identityUserId != Guid.Empty;

    public sealed record UpdateAccountUiPreferencesRequest(string? Locale, string? ColorTheme, string? Appearance, JsonElement Avatar);
}
