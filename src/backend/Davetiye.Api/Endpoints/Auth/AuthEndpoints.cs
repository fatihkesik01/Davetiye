using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Davetiye.Api.Endpoints.Auth;

/// <summary>
/// M6a's email/password auth endpoints (register/confirm-email/login/logout/request-password-reset/
/// reset-password). Deliberately references only <see cref="Davetiye.Application"/> (the
/// already-existing <see cref="IAuthAccountService"/> contract and its request/result DTOs) and this
/// Api project's own <see cref="AntiforgeryEndpointFilter"/>/<see cref="RequireHttpsInProductionFilter"/>
/// - never the composition-root-only Infrastructure layer directly (Davetiye.ArchitectureTests'
/// "Api uses Infrastructure only from the composition root" rule). Rate-limit policy names live in
/// that layer, so Program.cs (the composition root) reads them and passes the plain string values in
/// here, per the convention already documented on <c>AuthRateLimitPolicyNames</c>.
///
/// Google OAuth, Admin bootstrap and TOTP/MFA are explicitly out of scope (M6b); nothing here wires
/// them.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps the auth endpoints under <paramref name="apiV1Group"/> (expected to already carry the
    /// <c>/api/v1</c> prefix), as a <c>/auth</c> subgroup.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string registerRateLimitPolicy,
        string loginRateLimitPolicy,
        string passwordResetRequestRateLimitPolicy,
        string emailConfirmationRateLimitPolicy,
        string passwordResetConfirmRateLimitPolicy,
        string superAdminClaimType,
        string superAdminClaimValue,
        string mfaClaimType,
        string mfaClaimValue)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(registerRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordResetRequestRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(emailConfirmationRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordResetConfirmRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(superAdminClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(superAdminClaimValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(mfaClaimValue);

        // docs/THREAT_MODEL.md §5/§12: the entire auth-sensitive route group is fail-closed to raw
        // (non-forwarded-HTTPS) requests in Production; health/live/ready endpoints are mapped
        // entirely outside this group so they remain reachable for private smoke/health checks.
        var group = apiV1Group
            .MapGroup("/auth")
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(registerRateLimitPolicy);

        group.MapPost("/confirm-email", ConfirmEmailAsync)
            .RequireRateLimiting(emailConfirmationRateLimitPolicy);

        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(loginRateLimitPolicy);

        // This PII-free projection only decides which browser shell may render. It is not a
        // resource-authorization substitute: future endpoints retain ownership/policy checks.
        group.MapGet("/session", (HttpContext httpContext, IAuthSessionAccessService sessionAccessService,
                CancellationToken cancellationToken) => GetSessionAsync(
                httpContext, sessionAccessService, superAdminClaimType, superAdminClaimValue,
                mfaClaimType, mfaClaimValue, cancellationToken));

        // The only endpoint in this group that acts on an already-authenticated cookie session, so
        // it is the only one that needs the antiforgery filter (docs/THREAT_MODEL.md §5: antiforgery
        // applies to authenticated-cookie unsafe endpoints). Register/login/reset-password are
        // pre-auth and do NOT carry the filter, but that is not because "credentials are in the
        // body" - a plain cross-site HTML <form> POST also puts its fields in the body and would
        // still be a CSRF vector if that were the reasoning. The two real reasons these three are
        // safe without it:
        // (a) all three bind their request via [FromBody] (JSON), and a plain cross-site HTML
        //     <form> can only submit as application/x-www-form-urlencoded, multipart/form-data or
        //     text/plain - none of which model binding here accepts - so an attacker's <form> POST
        //     cannot invoke these endpoints with attacker-chosen field values; and
        // (b) CORS is an exact-origin allowlist (DavetiyeCorsOptions/AddCors below), so a
        //     cross-origin fetch() sending application/json triggers a CORS preflight that fails for
        //     any origin not on that allowlist, blocking the one remaining way to reach a
        //     JSON-bound endpoint cross-origin.
        // If either property changes in the future (e.g. adding [FromForm] support here, or
        // widening CORS), this reasoning breaks and these endpoints would need antiforgery too - see
        // the regression test proving the form-urlencoded case is rejected.
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // docs/THREAT_MODEL.md §9: the response is identical regardless of whether the email exists -
        // IAuthAccountService.RequestPasswordResetAsync has no outcome to branch on, by design.
        group.MapPost("/request-password-reset", RequestPasswordResetAsync)
            .RequireRateLimiting(passwordResetRequestRateLimitPolicy);

        group.MapPost("/reset-password", ResetPasswordAsync)
            .RequireRateLimiting(passwordResetConfirmRateLimitPolicy);

        return apiV1Group;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterAccountRequest request,
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        var result = await authAccountService.RegisterAsync(request, cancellationToken);

        return result.Outcome switch
        {
            RegisterAccountOutcome.Succeeded => Results.Ok(),
            RegisterAccountOutcome.EmailAlreadyRegistered => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered.",
                detail: string.Join(" ", result.Errors)),
            RegisterAccountOutcome.InvalidPassword => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid password.",
                detail: string.Join(" ", result.Errors)),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid registration request.",
                detail: string.Join(" ", result.Errors)),
        };
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        var result = await authAccountService.ConfirmEmailAsync(request, cancellationToken);

        return result.Outcome switch
        {
            ConfirmEmailOutcome.Succeeded => Results.Ok(),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid or expired email confirmation request."),
        };
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        var result = await authAccountService.LoginAsync(request, cancellationToken);

        return result.Outcome switch
        {
            LoginOutcome.Succeeded => Results.Ok(),
            LoginOutcome.InvalidCredentials => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid email or password."),
            LoginOutcome.LockedOut => Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account temporarily locked due to repeated failed attempts."),
            LoginOutcome.Banned => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Account is banned."),
            // 403, matching Banned: the account exists and the credential attempt reached the
            // point of being evaluated against it, but a policy (not the credential itself) is
            // what blocks access. See AuthAccountService.LoginAsync for why distinguishing this
            // from InvalidCredentials is not an enumeration violation here.
            LoginOutcome.EmailNotConfirmed => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email confirmation required before logging in."),
            // The password check succeeded, but the account has two-factor enabled (M6b): sign-in
            // is not complete yet. The client must submit a TOTP/recovery code to
            // /api/v1/admin/mfa/login/complete before an "MfaComplete"-eligible session exists.
            LoginOutcome.RequiresTwoFactor => Results.Ok(new { requiresTwoFactor = true }),
            _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext httpContext,
        IAuthSessionAccessService sessionAccessService,
        string superAdminClaimType,
        string superAdminClaimValue,
        string mfaClaimType,
        string mfaClaimValue,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        var identityUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!(httpContext.User.Identity?.IsAuthenticated ?? false) || !Guid.TryParse(identityUserId, out var userId))
        {
            return Results.Ok(new { authenticated = false, access = "none" });
        }

        var snapshot = await sessionAccessService.GetAccessAsync(
            userId,
            httpContext.User.HasClaim(superAdminClaimType, superAdminClaimValue),
            httpContext.User.HasClaim(mfaClaimType, mfaClaimValue),
            cancellationToken);

        return Results.Ok(new
        {
            authenticated = true,
            access = snapshot.Access switch
            {
                SessionAccess.Creator => "creator",
                SessionAccess.MfaCompleteSuperAdmin => "mfa-complete-super-admin",
                _ => "none",
            },
        });
    }

    private static async Task<IResult> LogoutAsync(
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        await authAccountService.LogoutAsync(cancellationToken);

        return Results.Ok();
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        // Always the same 200 response, whatever the outcome, because the service method itself has
        // no outcome to branch on (docs/THREAT_MODEL.md §9 enumeration-safety).
        await authAccountService.RequestPasswordResetAsync(request, cancellationToken);

        return Results.Ok();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        [FromServices] IAuthAccountService authAccountService,
        CancellationToken cancellationToken)
    {
        var result = await authAccountService.ResetPasswordAsync(request, cancellationToken);

        return result.Outcome switch
        {
            ResetPasswordOutcome.Succeeded => Results.Ok(),
            ResetPasswordOutcome.InvalidPassword => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid password.",
                detail: string.Join(" ", result.Errors)),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid or expired password reset request."),
        };
    }
}
