using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Auth;

/// <summary>
/// M6b's Google OAuth endpoints. References only <see cref="Davetiye.Application"/> (the
/// <see cref="IGoogleSignInService"/> contract) and this Api project's own
/// <see cref="RequireHttpsInProductionFilter"/> — never the composition-root-only Infrastructure
/// layer directly, matching <c>AuthEndpoints</c>' convention. The authentication scheme name lives
/// in that layer (<c>GoogleAuthenticationSchemeNames</c>), so Program.cs (the composition root)
/// reads it and passes the plain string value in here, exactly like the rate-limit policy names
/// already do.
///
/// Only mapped by Program.cs when <c>GoogleAuthOptions.Enabled</c> is true; when Google sign-in is
/// disabled (the default), these routes simply do not exist and a request to them is an ordinary 404
/// rather than a 500 from challenging an unregistered authentication scheme.
/// </summary>
public static class GoogleAuthEndpoints
{
    public static IEndpointRouteBuilder MapGoogleAuthEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string googleAuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleAuthenticationScheme);

        var group = apiV1Group
            .MapGroup("/auth/google")
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        // Initiates the OAuth flow. GET is intentional: this endpoint has no side effect of its own
        // beyond the redirect (docs/THREAT_MODEL.md §6's state/nonce/PKCE protections apply to the
        // handshake the framework performs after this point, not to this initiating request).
        group.MapGet("/challenge", (
            string? returnUrl,
            [FromServices] IGoogleSignInService googleSignInService) =>
        {
            // The URL the Google authentication HANDLER (not this endpoint) redirects the browser to
            // once it has exchanged the code and established the External-scheme cookie — distinct
            // from GoogleAuthOptions.CallbackBaseUrl, which is the URL Google itself redirects back
            // to and which the handler intercepts directly, never reaching minimal API routing.
            var completeUrl = "/api/v1/auth/google/complete?returnUrl=" +
                Uri.EscapeDataString(returnUrl ?? string.Empty);

            var properties = new AuthenticationProperties { RedirectUri = completeUrl };
            foreach (var (key, value) in googleSignInService.BuildChallengePropertyItems(googleAuthenticationScheme))
            {
                properties.Items[key] = value;
            }

            return Results.Challenge(properties, [googleAuthenticationScheme]);
        });

        group.MapGet("/complete", CompleteAsync);

        return apiV1Group;
    }

    private static async Task<IResult> CompleteAsync(
        string? returnUrl,
        [FromServices] IGoogleSignInService googleSignInService,
        CancellationToken cancellationToken)
    {
        var result = await googleSignInService.CompleteSignInAsync(returnUrl, cancellationToken);

        return result.Outcome switch
        {
            GoogleSignInOutcome.Succeeded => Results.Redirect(result.RedirectUrl ?? "/"),
            GoogleSignInOutcome.EmailAlreadyRegisteredWithoutLinking => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists and is not linked to Google."),
            GoogleSignInOutcome.EmailNotVerifiedByProvider => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Google did not report this account's email as verified."),
            GoogleSignInOutcome.Banned => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Account is banned."),
            GoogleSignInOutcome.ExternalLoginInfoMissing => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Google sign-in session expired or invalid."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Google sign-in failed."),
        };
    }
}
