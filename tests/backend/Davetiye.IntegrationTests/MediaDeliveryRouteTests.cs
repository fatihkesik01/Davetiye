using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Davetiye.Api.Endpoints.Invitations;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Application.Modules.Analytics.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.IntegrationTests;

public sealed class MediaDeliveryRouteTests
{
    private const string RouteCode = "public-code";
    private static readonly Guid InvitationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Public_delivery_is_bodyless_and_sets_no_store_no_cache_and_no_referrer()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/public/invitations/{RouteCode}/media/{AssetId}/delivery");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(request.Content);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var service = host.Services.GetRequiredService<TestMediaDeliveryService>();
        Assert.Equal(RouteCode, service.PublicCode);
        Assert.Equal(AssetId, service.AssetId);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Public_delivery_maps_unavailable_upstream_result_to_503()
    {
        using var host = await StartHostAsync();
        var service = host.Services.GetRequiredService<TestMediaDeliveryService>();
        service.Outcome = "Unavailable";
        using var client = host.GetTestClient();

        using var response = await client.PostAsync($"/api/v1/public/invitations/{RouteCode}/media/{AssetId}/delivery", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Creator_media_list_requires_authentication_and_resolves_the_current_account()
    {
        using var host = await StartHostAsync();
        using var anonymous = host.GetTestClient();
        using var rejected = await anonymous.GetAsync($"/api/v1/creator/invitations/{InvitationId}/media");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var authenticated = host.GetTestClient();
        authenticated.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        using var response = await authenticated.GetAsync($"/api/v1/creator/invitations/{InvitationId}/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(TestAccountResolver.AccountId, host.Services.GetRequiredService<TestMediaLibraryService>().LastAccountId);
    }

    [Fact]
    public async Task Creator_media_list_forbids_an_authenticated_identity_without_a_current_account()
    {
        using var host = await StartHostAsync();
        host.Services.GetRequiredService<TestAccountResolver>().ResolveToNull = true;
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");

        using var response = await client.GetAsync($"/api/v1/creator/invitations/{InvitationId}/media");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaLibraryService>().ListCalls);
    }

    [Fact]
    public async Task Creator_media_mutation_without_antiforgery_header_does_not_reach_service()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v1/creator/invitations/{InvitationId}/media/{AssetId}/placement")
        {
            Content = JsonContent.Create(new { role = "Cover", sortOrder = 0 }),
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Services.GetRequiredService<TestMediaLibraryService>().MutationCalls);
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
                    services.AddSingleton<TestAccountResolver>();
                    services.AddSingleton<ICurrentAccountResolver>(provider => provider.GetRequiredService<TestAccountResolver>());
                    services.AddSingleton<IAccountReferenceValidator, TestAccountReferenceValidator>();
                    services.AddSingleton<IInvitationOwnershipValidator, TestInvitationOwnershipValidator>();
                    services.AddSingleton<TestMediaDeliveryService>();
                    services.AddSingleton<IPublicMediaDeliveryService>(provider => provider.GetRequiredService<TestMediaDeliveryService>());
                    services.AddSingleton<TestMediaLibraryService>();
                    services.AddSingleton<ICreatorMediaLibraryService>(provider => provider.GetRequiredService<TestMediaLibraryService>());
                    services.AddSingleton<IPublicInvitationService, TestPublicInvitationService>();
                    services.AddSingleton<IInvitationStatisticsReader, TestInvitationStatisticsReader>();
                    services.AddSingleton<IInvitationViewCounter, TestInvitationViewCounter>();
                    services.AddSingleton(new PublicInvitationPresentationSettings("https://www.example.test", "https://app.example.test"));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var api = endpoints.MapGroup("/api/v1");
                        api.MapPublicInvitationEndpoints("media-test");
                        api.MapCreatorMediaLibraryEndpoints("media-test");
                    });
                });
            })
            .StartAsync();
        return host;
    }

    private sealed class TestMediaDeliveryService : IPublicMediaDeliveryService
    {
        public int Calls { get; private set; }
        public string Outcome { get; set; } = "Succeeded";
        public string? PublicCode { get; private set; }
        public Guid AssetId { get; private set; }

        public Task<MediaDeliveryResult> CreateAsync(string publicCode, Guid assetId, CancellationToken cancellationToken)
        {
            Calls++;
            PublicCode = publicCode;
            AssetId = assetId;
            return Task.FromResult(new MediaDeliveryResult(Outcome, "image",
                new Uri("https://media.example.test/short-lived"), DateTimeOffset.UtcNow.AddSeconds(30)));
        }
    }

    private sealed class TestMediaLibraryService : ICreatorMediaLibraryService
    {
        public Guid LastAccountId { get; private set; }
        public int ListCalls { get; private set; }
        public int MutationCalls { get; private set; }

        public Task<CreatorMediaLibraryResult> ListAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
        {
            ListCalls++;
            LastAccountId = accountId;
            return Task.FromResult(new CreatorMediaLibraryResult("Succeeded", []));
        }

        public Task<CreatorMediaLibraryResult> SetPlacementAsync(CreatorMediaPlacementCommand command, CancellationToken cancellationToken)
        {
            MutationCalls++;
            return Task.FromResult(new CreatorMediaLibraryResult("Succeeded"));
        }

        public Task<CreatorMediaLibraryResult> DeleteAsync(CreatorMediaDeleteCommand command, CancellationToken cancellationToken)
        {
            MutationCalls++;
            return Task.FromResult(new CreatorMediaLibraryResult("Deleting"));
        }
    }

    private sealed class TestAccountResolver : ICurrentAccountResolver
    {
        public static readonly Guid AccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public bool ResolveToNull { get; set; }
        public Task<Guid?> ResolveAccountIdAsync(Guid identityUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(ResolveToNull ? null : AccountId);
    }

    private sealed class TestPublicInvitationService : IPublicInvitationService
    {
        public Task<PublicInvitationReadResult> GetAsync(string publicCode, CancellationToken cancellationToken) =>
            Task.FromResult(new PublicInvitationReadResult(PublicInvitationOutcome.NotFound));
    }

    private sealed class TestAccountReferenceValidator : IAccountReferenceValidator
    {
        public Task<AccountReferenceStatus> GetStatusAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(AccountReferenceStatus.Verified);
    }

    private sealed class TestInvitationOwnershipValidator : IInvitationOwnershipValidator
    {
        public Task<bool> IsOwnedByAccountAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class TestInvitationStatisticsReader : IInvitationStatisticsReader
    {
        public Task<InvitationAggregateStatistics> ReadAsync(Guid invitationId, CancellationToken cancellationToken) =>
            Task.FromResult(new InvitationAggregateStatistics(0, 0, 0, 0, 0, 0));
    }

    private sealed class TestInvitationViewCounter : IInvitationViewCounter
    {
        public Task RecordAsync(Guid invitationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> ReadAsync(Guid invitationId, CancellationToken cancellationToken) => Task.FromResult(0L);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MediaDeliveryRouteTest";
        private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
