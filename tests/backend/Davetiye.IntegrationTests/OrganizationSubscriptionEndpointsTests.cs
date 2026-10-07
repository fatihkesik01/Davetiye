using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Davetiye.Api.Endpoints.Payments;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Xunit;

namespace Davetiye.IntegrationTests;

public sealed class OrganizationSubscriptionEndpointsTests
{
    private static readonly Guid SubscriptionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OwnerAccountId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset PaidThrough = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_route_serializes_server_lifecycle_statuses_and_private_headers()
    {
        using var host = await StartHostAsync();
        using var client = AuthenticatedClient(host);
        var service = host.Services.GetRequiredService<TestSubscriptionLifecycleService>();

        foreach (var status in new[]
                 {
                     OrganizationSubscriptionAccessStatus.Active,
                     OrganizationSubscriptionAccessStatus.Canceled,
                     OrganizationSubscriptionAccessStatus.Expired
                 })
        {
            service.Snapshot = CreateSnapshot(status);
            using var response = await client.GetAsync($"/api/v1/payments/organization-subscription?accountId={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
            Assert.Equal("Organization", json.RootElement.GetProperty("planDisplayName").GetString());
            Assert.Equal(1199m, json.RootElement.GetProperty("priceAmount").GetDecimal());
            Assert.Equal("TRY", json.RootElement.GetProperty("currency").GetString());
            Assert.Equal("monthly", json.RootElement.GetProperty("billingPeriod").GetString());
            Assert.False(json.RootElement.TryGetProperty("accountId", out _));
            Assert.False(json.RootElement.TryGetProperty("planId", out _));
            Assert.False(json.RootElement.TryGetProperty("providerSubscriptionId", out _));
        }

        service.Snapshot = null;
        using var emptyResponse = await client.GetAsync("/api/v1/payments/organization-subscription");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Equal("null", await emptyResponse.Content.ReadAsStringAsync());
        Assert.Equal(OwnerAccountId, service.LastReadAccountId);
    }

    [Fact]
    public async Task Cancellation_requires_authentication_antiforgery_and_owner_scoped_subscription()
    {
        using var host = await StartHostAsync();
        var path = $"/api/v1/payments/organization-subscription/{SubscriptionId}/cancel";
        using (var anonymous = host.GetTestClient())
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(path, null)).StatusCode);

        using var owner = AuthenticatedClient(host);
        var missingCsrf = await owner.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestSubscriptionLifecycleService>().CancellationCalls);

        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "valid-test-token");
        var success = await owner.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.True(success.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", success.Headers.Pragma.ToString(), StringComparison.OrdinalIgnoreCase);
        using (var json = JsonDocument.Parse(await success.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Applied", json.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(PaidThrough, json.RootElement.GetProperty("paidThroughAtUtc").GetDateTimeOffset());
            Assert.False(json.RootElement.TryGetProperty("enqueueCancellationNotice", out _));
        }

        var service = host.Services.GetRequiredService<TestSubscriptionLifecycleService>();
        Assert.Equal(1, service.CancellationCalls);
        Assert.Equal(OwnerAccountId, service.LastCancellationAccountId);

        var replay = await owner.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var json = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()))
            Assert.Equal("Duplicate", json.RootElement.GetProperty("outcome").GetString());

        var foreign = await owner.PostAsync($"/api/v1/payments/organization-subscription/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(OwnerAccountId, service.LastCancellationAccountId);
    }

    [Fact]
    public async Task Superadmin_is_forbidden_from_subscription_owner_routes()
    {
        using var host = await StartHostAsync();
        using var superadmin = AuthenticatedClient(host, superadmin: true);
        var read = await superadmin.GetAsync("/api/v1/payments/organization-subscription");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        superadmin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "valid-test-token");
        var cancel = await superadmin.PostAsync($"/api/v1/payments/organization-subscription/{SubscriptionId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestSubscriptionLifecycleService>().CancellationCalls);
    }

    [Fact]
    public async Task Subscription_routes_require_https_in_production()
    {
        using var host = await StartHostAsync("Production");
        using var client = AuthenticatedClient(host);
        var read = await client.GetAsync("/api/v1/payments/organization-subscription");
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Null(host.Services.GetRequiredService<TestSubscriptionLifecycleService>().LastReadAccountId);

        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "valid-test-token");
        var cancel = await client.PostAsync($"/api/v1/payments/organization-subscription/{SubscriptionId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestSubscriptionLifecycleService>().CancellationCalls);
    }

    private static OrganizationSubscriptionAccessSnapshot CreateSnapshot(string status) => new(
        SubscriptionId, OwnerAccountId, Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), PaidThrough,
        status == OrganizationSubscriptionAccessStatus.Canceled, null, "Organization", 1199m, "TRY", "monthly", status);

    private static HttpClient AuthenticatedClient(IHost host, bool superadmin = false)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        if (superadmin) client.DefaultRequestHeaders.Add("X-Test-Superadmin", "true");
        return client;
    }

    private static async Task<IHost> StartHostAsync(string environment = "Development") => await new HostBuilder()
        .ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.UseEnvironment(environment);
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorization();
                services.AddRateLimiter(options =>
                {
                    options.AddPolicy("subscription-read", _ => RateLimitPartition.GetNoLimiter("read"));
                    options.AddPolicy("subscription-write", _ => RateLimitPartition.GetNoLimiter("write"));
                });
                services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
                services.AddSingleton<IAntiforgery, TestAntiforgery>();
                services.AddSingleton<ICurrentAccountResolver, TestCurrentAccountResolver>();
                services.AddSingleton<TestSubscriptionLifecycleService>();
                services.AddSingleton<IOrganizationSubscriptionLifecycleService>(provider =>
                    provider.GetRequiredService<TestSubscriptionLifecycleService>());
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1")
                    .MapOrganizationSubscriptionEndpoints("subscription-read", "subscription-write"));
            });
        }).StartAsync();

    private sealed class TestSubscriptionLifecycleService : IOrganizationSubscriptionLifecycleService
    {
        public OrganizationSubscriptionAccessSnapshot? Snapshot { get; set; } = CreateSnapshot(OrganizationSubscriptionAccessStatus.Active);
        public int CancellationCalls { get; private set; }
        public Guid? LastReadAccountId { get; private set; }
        public Guid? LastCancellationAccountId { get; private set; }

        public Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(Guid accountId, CancellationToken cancellationToken)
        {
            LastReadAccountId = accountId;
            return Task.FromResult(Snapshot);
        }

        public Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(Guid accountId, Guid subscriptionId,
            CancellationToken cancellationToken)
        {
            CancellationCalls++;
            LastCancellationAccountId = accountId;
            return Task.FromResult(new OrganizationSubscriptionCommandResult(
                accountId != OwnerAccountId || subscriptionId != SubscriptionId
                    ? OrganizationSubscriptionCommandOutcome.NotFound
                    : CancellationCalls == 1
                        ? OrganizationSubscriptionCommandOutcome.Applied
                        : OrganizationSubscriptionCommandOutcome.Duplicate,
                false, false, true, PaidThrough));
        }

        public Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
            VerifiedOrganizationSubscriptionActivation activation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
            VerifiedOrganizationSubscriptionRenewal renewal, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestCurrentAccountResolver : ICurrentAccountResolver
    {
        public Task<Guid?> ResolveAccountIdAsync(Guid identityUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(identityUserId == TestAuthenticationHandler.IdentityId ? OwnerAccountId : null);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OrganizationSubscriptionRouteTest";
        public static readonly Guid IdentityId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, IdentityId.ToString()) };
            if (Request.Headers.ContainsKey("X-Test-Superadmin"))
                claims.Add(new(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class TestAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new("request", "cookie", "field", "X-CSRF-TOKEN");
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => GetAndStoreTokens(httpContext);
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }
}
