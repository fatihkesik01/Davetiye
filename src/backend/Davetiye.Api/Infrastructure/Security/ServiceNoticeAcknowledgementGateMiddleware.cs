using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

namespace Davetiye.Api.Infrastructure.Security;

/// <summary>
/// Keeps a legacy Creator session from reaching Creator APIs until the required service notice has
/// been explicitly acknowledged. It checks persistence on every request so an old cookie cannot
/// bypass the gate. Public guest traffic is anonymous and unaffected; Super Admin sessions bypass
/// the Creator-only gate.
/// </summary>
public sealed class ServiceNoticeAcknowledgementGateMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedBeforeAcknowledgement = new(StringComparer.Ordinal)
    {
        "/api/v1/auth/session",
        "/api/v1/auth/logout",
        "/api/v1/account/consents",
        "/api/v1/account/consents/service-notice",
        "/api/v1/account/consents/marketing",
        "/api/v1/antiforgery/token",
    };

    public async Task InvokeAsync(HttpContext context, IAccountConsentService consentService)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!(context.User.Identity?.IsAuthenticated ?? false) ||
            context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count is null or 0 ||
            !path.StartsWith("/api/v1/", StringComparison.Ordinal) ||
            AllowedBeforeAcknowledgement.Contains(path) ||
            context.User.HasClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue) ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var identityUserId) ||
            await consentService.IsServiceNoticeAcknowledgedAsync(identityUserId, context.RequestAborted))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await Results.Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: "Required service notice acknowledgement required.",
            detail: "Acknowledge the service notice before continuing to use Creator features.")
            .ExecuteAsync(context);
    }
}
