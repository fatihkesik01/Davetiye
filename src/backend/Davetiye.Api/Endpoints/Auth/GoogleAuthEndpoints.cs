using System.Security.Claims;
using System.Text.Json.Serialization;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

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
        string googleAuthenticationScheme,
        string linkConfirmRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkConfirmRateLimitPolicy);

        var group = apiV1Group
            .MapGroup("/auth/google")
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        // Use a native same-origin HTML form POST. A top-level cross-site link/form cannot read the
        // antiforgery token issued to this origin, while the OAuth handler can still return its
        // ordinary 302 and establish correlation/state cookies for the browser navigation.
        group.MapPost("/challenge", async (
            HttpContext httpContext,
            [FromServices] IGoogleSignInService googleSignInService,
            CancellationToken cancellationToken) =>
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            if (!form.TryGetValue("accountType", out var accountTypeValue) ||
                !form.TryGetValue("serviceNoticeAcknowledged", out var serviceNoticeValue) ||
                !bool.TryParse(serviceNoticeValue, out var serviceNoticeAcknowledged))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Account type and explicit service-notice choice are required.");
            }

            var accountType = accountTypeValue.ToString();
            var returnUrl = form.TryGetValue("returnUrl", out var returnUrlValue) ? returnUrlValue.ToString() : null;
            var marketingOptIn = false;
            if (form.TryGetValue("marketingOptIn", out var marketingValue) &&
                !bool.TryParse(marketingValue, out marketingOptIn))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Marketing preference must be a boolean value.");
            }

            // The URL the Google authentication HANDLER (not this endpoint) redirects the browser to
            // once it has exchanged the code and established the External-scheme cookie — distinct
            // from GoogleAuthOptions.CallbackBaseUrl, which is the URL Google itself redirects back
            // to and which the handler intercepts directly, never reaching minimal API routing.
            const string completeUrl = "/api/v1/auth/google/complete";

            var properties = new AuthenticationProperties { RedirectUri = completeUrl };
            var protectedItems = googleSignInService.BuildChallengePropertyItems(
                googleAuthenticationScheme, accountType, returnUrl,
                serviceNoticeAcknowledged, marketingOptIn);
            if (protectedItems is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Account type must be 'Individual' or 'Organization'.");
            }

            foreach (var (key, value) in protectedItems)
            {
                properties.Items[key] = value;
            }

            return Results.Challenge(properties, [googleAuthenticationScheme]);
        })
            .Accepts<GoogleChallengeFormData>("application/x-www-form-urlencoded")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRateLimiting(linkConfirmRateLimitPolicy)
            .AddEndpointFilter<FormAntiforgeryEndpointFilter>();

        group.MapGet("/complete", CompleteAsync);

        // The second, explicit step of the M1 account-linking flow (docs/ROADMAP.md §4a's
        // 2026-09-29 decision): the caller must already be authenticated as the account being linked
        // into (RequireAuthorization, checked against the Application cookie scheme) and provide the
        // current password in the request body before this attaches the still-pending Google
        // external login. It is antiforgery-protected like /auth/logout and rate-limited like login,
        // since it is an authenticated-cookie endpoint with a credential check and a real side
        // effect.
        group.MapPost("/link/confirm", ConfirmLinkAsync)
            .RequireAuthorization()
            .RequireRateLimiting(linkConfirmRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return apiV1Group;
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext httpContext,
        [FromServices] IGoogleSignInService googleSignInService,
        CancellationToken cancellationToken)
    {
        GoogleSignInResult? result = null;
        try
        {
            result = await googleSignInService.CompleteSignInAsync(cancellationToken);
        }
        finally
        {
            // AccountLinkRequired intentionally retains this short-lived external principal for
            // the password-confirmation step. Every other outcome is terminal and clears it; if
            // completion throws before producing a result, clear it fail-closed as well.
            if (result?.Outcome != GoogleSignInOutcome.AccountLinkRequired)
                await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        var completed = result ?? throw new InvalidOperationException("Google sign-in returned no outcome.");
        return completed.Outcome switch
        {
            GoogleSignInOutcome.Succeeded => Results.Redirect(completed.RedirectUrl ?? "/"),
            GoogleSignInOutcome.AccountLinkRequired => Results.Redirect(completed.RedirectUrl ?? "/"),
            GoogleSignInOutcome.SuperAdminGoogleSignInNotAllowed => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Google sign-in is not available for this account."),
            GoogleSignInOutcome.EmailNotVerifiedByProvider => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Google did not report this account's email as verified."),
            GoogleSignInOutcome.ServiceNoticeAcknowledgementRequired => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Required service notice acknowledgement is missing."),
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

    private static async Task<IResult> ConfirmLinkAsync(
        ConfirmGoogleAccountLinkRequest request,
        HttpContext httpContext,
        [FromServices] IGoogleSignInService googleSignInService,
        CancellationToken cancellationToken)
    {
        var identityUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(identityUserId, out var userId))
        {
            await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.Unauthorized();
        }

        GoogleAccountLinkResult result;
        try
        {
            result = await googleSignInService.ConfirmLinkAsync(userId, request.Password, cancellationToken);
        }
        finally
        {
            // Password confirmation is terminal whether it succeeds or fails; a retry must start
            // a fresh OAuth flow after re-authenticating the existing account.
            await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        return result.Outcome switch
        {
            GoogleAccountLinkOutcome.Succeeded => Results.Ok(),
            GoogleAccountLinkOutcome.NoPendingExternalLogin => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No pending Google sign-in to link. Start Google sign-in again first."),
            GoogleAccountLinkOutcome.EmailMismatch => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Google account's email does not match this account."),
            GoogleAccountLinkOutcome.AlreadyLinkedToAnotherAccount => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This Google account is already linked to a different account."),
            GoogleAccountLinkOutcome.SuperAdminLinkingNotAllowed => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Google sign-in linking is not available for this account."),
            GoogleAccountLinkOutcome.InvalidPassword => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The current password is incorrect."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Could not link the Google account."),
        };
    }
}

/// <summary>OpenAPI form shape; the standard antiforgery hidden field is validated separately.</summary>
public sealed record GoogleChallengeFormData(
    string AccountType,
    string? ReturnUrl,
    bool ServiceNoticeAcknowledged,
    [property: JsonPropertyName("__RequestVerificationToken")] string AntiforgeryToken,
    bool? MarketingOptIn = false);
