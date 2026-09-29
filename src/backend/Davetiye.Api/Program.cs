using Davetiye.Api.Endpoints.Auth;
using Davetiye.Api.Infrastructure.Correlation;
using Davetiye.Api.Infrastructure.ErrorHandling;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Api.Infrastructure.Routing;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Infrastructure;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Request body size: a sane, configurable ceiling (never a hardcoded number used directly by
// Kestrel). The bound/validated RequestLimitsOptions below is the source of truth; Kestrel is
// configured from the same raw configuration because Kestrel limits must be set before the host
// is built, ahead of when validated IOptions<T> becomes resolvable.
builder.WebHost.ConfigureKestrel((context, kestrelOptions) =>
{
    kestrelOptions.Limits.MaxRequestBodySize = context.Configuration.GetValue(
        $"{RequestLimitsOptions.SectionName}:{nameof(RequestLimitsOptions.MaxRequestBodyBytes)}",
        RequestLimitsOptions.DefaultMaxRequestBodyBytes);
});

builder.Services.AddApiHostingOptions(builder.Configuration);

// Forwarded headers: nothing is trusted unless explicitly configured (fail closed). The VPS
// deployment target puts Nginx on the same host in front of Kestrel (docs/DEPLOYMENT.md), so only
// operator-configured local/known proxies are ever honored for X-Forwarded-For/-Proto.
builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersOptions =>
{
    forwardedHeadersOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();

    var trustedProxySection = builder.Configuration.GetSection(TrustedProxyOptions.SectionName);
    var configuredNetworks = trustedProxySection
        .GetSection(nameof(TrustedProxyOptions.Networks))
        .Get<string[]>() ?? [];

    foreach (var network in configuredNetworks)
    {
        if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
        {
            forwardedHeadersOptions.KnownIPNetworks.Add(parsedNetwork);
        }
    }

    forwardedHeadersOptions.ForwardLimit = trustedProxySection.GetValue(
        nameof(TrustedProxyOptions.ForwardLimit),
        1);
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ProblemDetailsConfiguration.AddCorrelationId;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi();

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<DavetiyeDbContext>("database", tags: ["ready"]);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

// Forwarded headers must run before anything that reads Request.IsHttps/scheme (including
// RequireHttpsInProductionFilter on the auth route group below), so it stays first.
app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

// M6a security pipeline: CORS and authentication must both run before authorization.
// Rate limiting runs before authentication so an abusive caller is rejected before any Identity
// work (password hashing, DB lookups) happens on their behalf.
app.UseCors(CorsPolicyNames.Default);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

var apiV1 = app.MapGroup(ApiRoutes.V1Prefix);
apiV1.MapGet("/system/info", () => Results.Ok(new { service = "Davetiye.Api", apiVersion = "v1" }));

// Minimal antiforgery-token bootstrap endpoint: without this there is no way for a caller to ever
// obtain a valid cookie/token pair for AntiforgeryEndpointFilter-protected endpoints (e.g.
// /auth/logout) to validate against. GET is intentional (it only issues a token; it has no side
// effect that needs protecting itself).
apiV1.MapGet("/antiforgery/token", (IAntiforgery antiforgery, HttpContext httpContext) =>
{
    var tokens = antiforgery.GetAndStoreTokens(httpContext);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireRateLimiting(AuthRateLimitPolicyNames.AntiforgeryToken);

apiV1.MapAuthEndpoints(
    AuthRateLimitPolicyNames.Register,
    AuthRateLimitPolicyNames.Login,
    AuthRateLimitPolicyNames.PasswordResetRequest,
    AuthRateLimitPolicyNames.EmailConfirmation,
    AuthRateLimitPolicyNames.PasswordResetConfirm,
    SuperAdminClaimNames.SuperAdmin,
    SuperAdminClaimNames.SuperAdminClaimValue,
    SuperAdminClaimNames.AuthenticationMethodReference,
    SuperAdminClaimNames.MfaAmrValue);

apiV1.MapAdminMfaEndpoints(
    AuthorizationPolicyNames.SuperAdminOnly,
    AuthRateLimitPolicyNames.TwoFactorLoginComplete,
    AuthRateLimitPolicyNames.AdminMfaVerify);

// Resolving IOptions<GoogleAuthOptions> here (rather than only relying on ValidateOnStart's hosted
// service) forces GoogleAuthOptionsValidator to run deterministically before any request is served:
// an enabled-but-misconfigured Production setting throws here and the process never reaches
// app.Run() (docs/THREAT_MODEL.md §6/§12 gate 3 "Google config fail-closed"). When Google sign-in is
// disabled (the default), this is always valid and the routes below are simply never mapped.
var googleAuthOptions = app.Services.GetRequiredService<IOptions<GoogleAuthOptions>>().Value;
if (googleAuthOptions.Enabled)
{
    apiV1.MapGoogleAuthEndpoints(GoogleAuthenticationSchemeNames.Google);
}

app.Run();

public partial class Program;
