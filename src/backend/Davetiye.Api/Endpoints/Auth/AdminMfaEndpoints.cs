using System.Security.Claims;
using Davetiye.Api.Infrastructure.Antiforgery;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Davetiye.Api.Endpoints.Auth;

/// <summary>
/// M6b's Super Admin TOTP/recovery-code MFA endpoints: enroll, verify-and-enable, and two-factor
/// login completion. References only <see cref="Davetiye.Application"/> (the
/// <see cref="IAdminMfaService"/> contract) and this Api project's own endpoint filters — never the
/// composition-root-only Infrastructure layer directly. The two policy/rate-limit names live in that
/// layer; Program.cs (the composition root) reads them and passes the plain string values in here,
/// matching <c>AuthEndpoints</c>'s rate-limit-policy-name convention.
///
/// This is the foundation/contract for Admin MFA, not a business Administration module: no plan
/// management, ban creation or audit-log UI lives here.
/// </summary>
public static class AdminMfaEndpoints
{
    public static IEndpointRouteBuilder MapAdminMfaEndpoints(
        this IEndpointRouteBuilder apiV1Group,
        string superAdminOnlyPolicy,
        string twoFactorLoginCompleteRateLimitPolicy,
        string adminMfaVerifyRateLimitPolicy)
    {
        ArgumentNullException.ThrowIfNull(apiV1Group);
        ArgumentException.ThrowIfNullOrWhiteSpace(superAdminOnlyPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(twoFactorLoginCompleteRateLimitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminMfaVerifyRateLimitPolicy);

        var group = apiV1Group
            .MapGroup("/admin/mfa")
            .AddEndpointFilter<RequireHttpsInProductionFilter>();

        // Enroll/verify act on an already-authenticated cookie session (a Super Admin who logged in
        // with just a password, since 2FA is not yet enabled for them), so both carry antiforgery,
        // matching /auth/logout's convention.
        //
        // /enroll deliberately carries no rate-limit policy: it only issues a fresh authenticator
        // secret to a caller who already holds the "SuperAdminOnly" cookie (no guessable code is
        // accepted here, unlike /verify), so there is no brute-force surface a rate limit would
        // close — the worst a hostile caller with that cookie could do is churn secrets they could
        // already legitimately request one at a time.
        group.MapPost("/enroll", EnrollAsync)
            .RequireAuthorization(superAdminOnlyPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPost("/verify", VerifyAsync)
            .RequireAuthorization(superAdminOnlyPolicy)
            .RequireRateLimiting(adminMfaVerifyRateLimitPolicy)
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // Deliberately NOT RequireAuthorization: the caller here is only authenticated against the
        // intermediate TwoFactorUserIdScheme (established by AuthAccountService.LoginAsync via
        // DavetiyeSignInManager.SignInOrRequireTwoFactorAsync), not yet the main Application cookie
        // scheme — RequireAuthorization would reject every legitimate caller before the handler
        // ever runs. Safe without antiforgery for the same reason /auth/login is (see AuthEndpoints):
        // JSON-only body binding plus exact-origin CORS.
        group.MapPost("/login/complete", CompleteLoginAsync)
            .RequireRateLimiting(twoFactorLoginCompleteRateLimitPolicy);

        return apiV1Group;
    }

    private static async Task<IResult> EnrollAsync(
        HttpContext httpContext,
        [FromServices] IAdminMfaService adminMfaService,
        CancellationToken cancellationToken)
    {
        var result = await adminMfaService.EnrollAsync(GetIdentityUserId(httpContext), cancellationToken);

        return Results.Ok(new { sharedKey = result.SharedKey, authenticatorUri = result.AuthenticatorUri });
    }

    private static async Task<IResult> VerifyAsync(
        HttpContext httpContext,
        VerifyMfaRequestBody body,
        [FromServices] IAdminMfaService adminMfaService,
        CancellationToken cancellationToken)
    {
        var result = await adminMfaService.VerifyAndEnableAsync(
            GetIdentityUserId(httpContext), body.Code, cancellationToken);

        return result.Outcome switch
        {
            VerifyMfaOutcome.Succeeded => Results.Ok(new { recoveryCodes = result.RecoveryCodes }),
            VerifyMfaOutcome.AlreadyEnabled => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "MFA is already enabled for this account."),
            VerifyMfaOutcome.LockedOut => Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account temporarily locked due to repeated failed attempts."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid authenticator code."),
        };
    }

    private static async Task<IResult> CompleteLoginAsync(
        CompleteTwoFactorLoginRequest request,
        [FromServices] IAdminMfaService adminMfaService,
        CancellationToken cancellationToken)
    {
        var result = await adminMfaService.CompleteTwoFactorLoginAsync(request, cancellationToken);

        return result.Outcome switch
        {
            CompleteTwoFactorLoginOutcome.Succeeded => Results.Ok(),
            CompleteTwoFactorLoginOutcome.LockedOut => Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account temporarily locked due to repeated failed attempts."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid authenticator or recovery code."),
        };
    }

    /// <summary>
    /// ASP.NET Core Identity's cookie principal always carries the user id as the standard
    /// <see cref="ClaimTypes.NameIdentifier"/> claim; reading it directly here needs no reference to
    /// the Infrastructure layer. Safe to assume present and well-formed because this method is only
    /// ever called from handlers behind <c>RequireAuthorization(superAdminOnlyPolicy)</c>.
    /// </summary>
    private static Guid GetIdentityUserId(HttpContext httpContext) =>
        Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private sealed record VerifyMfaRequestBody(string Code);
}
