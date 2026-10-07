using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Davetiye.Api.Endpoints.Invitations;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Media;
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

public sealed class MediaVerificationEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Secret = "route-test-stream-webhook-secret-32-bytes";

    [Fact]
    public async Task Forged_webhook_signature_is_rejected_before_provider_service_is_called()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var request = CreateWebhookRequest("{\"creator\":\"11111111111111111111111111111111\",\"uid\":\"video-uid\",\"status\":{\"state\":\"ready\"},\"readyToStream\":true}");
        request.Headers.TryAddWithoutValidation("Webhook-Signature", $"time={Now.ToUnixTimeSeconds()},sig1=" + new string('0', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, host.Services.GetRequiredService<TestSignatureVerifier>().Calls);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaVerificationService>().WebhookCalls);
    }

    [Fact]
    public async Task Valid_webhook_verifies_exact_raw_body_before_processing()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        const string body = "{ \"creator\": \"11111111111111111111111111111111\", \"uid\": \"video-uid\", \"status\": { \"state\": \"ready\" }, \"readyToStream\": true }\n";
        using var request = CreateWebhookRequest(body);
        request.Headers.TryAddWithoutValidation("Webhook-Signature", Sign(Encoding.UTF8.GetBytes(body), Now.ToUnixTimeSeconds()));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verifier = host.Services.GetRequiredService<TestSignatureVerifier>();
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(Encoding.UTF8.GetBytes(body), verifier.LastRawBody);
        var service = host.Services.GetRequiredService<TestMediaVerificationService>();
        Assert.Equal(1, service.WebhookCalls);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), service.LastAssetId);
        Assert.Equal("video-uid", service.LastProviderUid);
    }

    [Fact]
    public async Task Chunked_webhook_over_64_kib_is_rejected_without_signature_or_provider_work()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/cloudflare/stream")
        {
            Content = new UnknownLengthContent(new byte[64 * 1024 + 1]),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestSignatureVerifier>().Calls);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaVerificationService>().WebhookCalls);
    }

    [Fact]
    public async Task Creator_finalize_rejects_unauthenticated_requests_before_service_call()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsync($"/api/v1/invitations/{Guid.NewGuid()}/media/{Guid.NewGuid()}/finalize", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaVerificationService>().FinalizeCalls);
    }

    [Fact]
    public async Task Creator_finalize_rejects_missing_antiforgery_header_before_service_call()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsync($"/api/v1/invitations/{Guid.NewGuid()}/media/{Guid.NewGuid()}/finalize", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaVerificationService>().FinalizeCalls);
    }

    [Fact]
    public async Task Creator_finalize_returns_forbidden_when_identity_has_no_account()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "test-antiforgery-value");
        host.Services.GetRequiredService<TestCurrentAccountResolver>().ResolveToNull = true;

        var response = await client.PostAsync($"/api/v1/invitations/{Guid.NewGuid()}/media/{Guid.NewGuid()}/finalize", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaVerificationService>().FinalizeCalls);
    }

    [Fact]
    public async Task Creator_finalize_uses_server_resolved_account_and_route_asset_ids()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "test-antiforgery-value");
        var invitationId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        var response = await client.PostAsync($"/api/v1/invitations/{invitationId}/media/{assetId}/finalize", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var service = host.Services.GetRequiredService<TestMediaVerificationService>();
        Assert.Equal(1, service.FinalizeCalls);
        Assert.Equal(TestCurrentAccountResolver.AccountId, service.LastAccountId);
        Assert.Equal(invitationId, service.LastInvitationId);
        Assert.Equal(assetId, service.LastAssetId);
    }

    private static HttpRequestMessage CreateWebhookRequest(string body) => new(HttpMethod.Post,
        "/api/v1/webhooks/cloudflare/stream")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string Sign(byte[] body, long timestamp)
    {
        var prefix = Encoding.ASCII.GetBytes($"{timestamp}.");
        var signedBytes = prefix.Concat(body).ToArray();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), signedBytes)).ToLowerInvariant();
        return $"time={timestamp},sig1={signature}";
    }

    private static async Task<IHost> StartHostAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddRateLimiter(options => options.AddPolicy("media-test",
                        _ => RateLimitPartition.GetNoLimiter("media-test")));
                    services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
                    services.AddSingleton<IAntiforgery, TestAntiforgery>();
                    services.AddSingleton<TestCurrentAccountResolver>();
                    services.AddSingleton<ICurrentAccountResolver>(provider => provider.GetRequiredService<TestCurrentAccountResolver>());
                    services.AddSingleton<TestMediaVerificationService>();
                    services.AddSingleton<IMediaVerificationService>(provider => provider.GetRequiredService<TestMediaVerificationService>());
                    services.AddSingleton<TestSignatureVerifier>();
                    services.AddSingleton<IStreamWebhookSignatureVerifier>(provider => provider.GetRequiredService<TestSignatureVerifier>());
                    services.AddSingleton<IClock>(new TestClock());
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1")
                        .MapCreatorMediaVerificationEndpoints("media-test"));
                });
            })
            .StartAsync();
        return host;
    }

    private sealed class TestSignatureVerifier : IStreamWebhookSignatureVerifier
    {
        public bool IsEnabled => true;
        public int Calls { get; private set; }
        public byte[]? LastRawBody { get; private set; }
        public bool IsValid(byte[] rawBody, string signatureHeader, DateTimeOffset now)
        {
            Calls++;
            LastRawBody = rawBody.ToArray();
            return StreamWebhookSignatureVerifier.Verify(rawBody, signatureHeader, Secret, now, TimeSpan.FromMinutes(5));
        }
    }

    private sealed class TestMediaVerificationService : IMediaVerificationService
    {
        public int WebhookCalls { get; private set; }
        public int FinalizeCalls { get; private set; }
        public Guid LastAssetId { get; private set; }
        public Guid LastInvitationId { get; private set; }
        public Guid LastAccountId { get; private set; }
        public string? LastProviderUid { get; private set; }
        public Task<MediaFinalizeResult> FinalizeImageAsync(Guid accountId, Guid invitationId, Guid assetId, CancellationToken cancellationToken)
        {
            FinalizeCalls++;
            LastAccountId = accountId;
            LastInvitationId = invitationId;
            LastAssetId = assetId;
            return Task.FromResult(MediaFinalizeResult.Ready);
        }
        public Task<MediaFinalizeResult> ProcessStreamWebhookAsync(Guid assetId, string providerUid, string state, bool readyToStream,
            CancellationToken cancellationToken)
        {
            WebhookCalls++;
            LastAssetId = assetId;
            LastProviderUid = providerUid;
            return Task.FromResult(MediaFinalizeResult.Ready);
        }
    }

    private sealed class TestCurrentAccountResolver : ICurrentAccountResolver
    {
        public static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public bool ResolveToNull { get; set; }
        public Task<Guid?> ResolveAccountIdAsync(Guid identityUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(!ResolveToNull && identityUserId == TestAuthenticationHandler.IdentityId ? AccountId : null);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MediaRouteTest";
        public static readonly Guid IdentityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, IdentityId.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => Now; }

    private sealed class TestAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new("request", "cookie", "field", "X-CSRF-TOKEN");
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => GetAndStoreTokens(httpContext);
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

