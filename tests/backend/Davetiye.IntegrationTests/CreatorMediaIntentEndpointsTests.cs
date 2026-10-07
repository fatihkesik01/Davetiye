using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CreatorMediaIntentEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string Password = "TestPassw0rd1";
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creator_intent_endpoint_requires_auth_and_antiforgery_then_uses_principal_account_and_returns_private_capability()
    {
        var mail = new CapturingEmailSender();
        var service = new CapturingMediaIntentService();
        await using var factory = new MediaApiFactory(connectionString, mail, service);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var anonymous = await PostIntentAsync(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var email = $"media-intent-{Guid.NewGuid():N}@example.test";
        await RegisterConfirmAndLoginAsync(client, mail, email);
        var creatorAccountId = await ResolveAccountIdAsync(connectionString, email);

        var invitationId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var missingCsrf = await PostIntentAsync(client, invitationId, idempotencyKey);
        Assert.True(missingCsrf.StatusCode == HttpStatusCode.BadRequest,
            await missingCsrf.Content.ReadAsStringAsync());
        Assert.Empty(service.Commands);

        var csrf = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/invitations/{invitationId}/media/intents")
        {
            Content = JsonContent.Create(new
            {
                kind = MediaKind.Image,
                presentationRole = MediaPresentationRole.Gallery,
                declaredByteLength = 4096,
                accountId = Guid.NewGuid(), // Unknown client ownership fields must never select the actor.
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(service.Result.IntentId, body.RootElement.GetProperty("intentId").GetGuid());
        Assert.Equal("https://upload.example.test/one-time-capability", body.RootElement.GetProperty("ingressUri").GetString());
        Assert.Equal("signed-capability", body.RootElement.GetProperty("ingressHeaders").GetProperty("X-Media-Capability").GetString());
        Assert.False(body.RootElement.TryGetProperty("providerObjectReference", out _));

        var command = Assert.Single(service.Commands);
        Assert.Equal(creatorAccountId, command.AccountId);
        Assert.Equal(invitationId, command.InvitationId);
        Assert.Equal(idempotencyKey, command.IdempotencyKey);
        Assert.Equal(MediaKind.Image, command.Kind);
        Assert.Equal(MediaPresentationRole.Gallery, command.PresentationRole);
        Assert.Equal(4096, command.DeclaredByteLength);
        Assert.False(string.IsNullOrWhiteSpace(service.ClientIpAddress));

        service.Outcome = CreatorMediaIntentOutcome.Created(service.Result with { Replayed = true },
            new MediaUploadCapability(new Uri("https://upload.example.test/one-time-capability"), service.Result.ExpiresAt,
                new Dictionary<string, string> { ["X-Media-Capability"] = "signed-capability" }));
        using var replay = await SendAuthorizedAsync(client, csrf, invitationId, idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replay.Headers.CacheControl?.NoStore);
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayBody.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(service.Result.AssetId, replayBody.RootElement.GetProperty("assetId").GetGuid());
    }

    [Fact]
    public async Task Creator_intent_endpoint_requires_idempotency_header_and_maps_denials_without_revealing_details()
    {
        var mail = new CapturingEmailSender();
        var service = new CapturingMediaIntentService
        {
            Outcome = CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.NotFound),
        };
        await using var factory = new MediaApiFactory(connectionString, mail, service);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterConfirmAndLoginAsync(client, mail, $"media-intent-{Guid.NewGuid():N}@example.test");
        var csrf = await GetCsrfTokenAsync(client);

        using var missingHeader = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/invitations/{Guid.NewGuid()}/media/intents")
        {
            Content = JsonContent.Create(new { kind = MediaKind.Image, presentationRole = MediaPresentationRole.Cover, declaredByteLength = 512 }),
        };
        missingHeader.Headers.Add("X-CSRF-TOKEN", csrf);
        using var invalid = await client.SendAsync(missingHeader);
        Assert.True(invalid.StatusCode == HttpStatusCode.BadRequest,
            await invalid.Content.ReadAsStringAsync());
        Assert.Empty(service.Commands);

        using var hidden = await SendAuthorizedAsync(client, csrf, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(hidden.Headers.CacheControl?.NoStore);

        service.Outcome = CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.UploadUnavailable);
        using var unavailable = await SendAuthorizedAsync(client, csrf, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.True(unavailable.Headers.CacheControl?.NoStore);
    }

    private static async Task<HttpResponseMessage> PostIntentAsync(HttpClient client, Guid invitationId, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invitations/{invitationId}/media/intents")
        {
            Content = JsonContent.Create(new { kind = MediaKind.Image, presentationRole = MediaPresentationRole.Gallery, declaredByteLength = 4096 }),
        };
        if (key is not null) request.Headers.Add("Idempotency-Key", key.Value.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, string csrf, Guid invitationId, Guid key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invitations/{invitationId}/media/intents")
        {
            Content = JsonContent.Create(new { kind = MediaKind.Image, presentationRole = MediaPresentationRole.Gallery, declaredByteLength = 4096 }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }

    private static async Task RegisterConfirmAndLoginAsync(HttpClient client, CapturingEmailSender emailSender, string email)
    {
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = Password, displayName = "Media Creator", accountType = "Individual", serviceNoticeAcknowledged = true });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var confirmation = emailSender.Sent.Single(message => message.ToEmail == email && message.Kind == EmailNotificationKinds.EmailConfirmation);
        var uri = new Uri(confirmation.Data["confirmationLink"]);
        Assert.Empty(uri.Query);
        var query = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
        var confirmed = await client.PostAsJsonAsync("/api/v1/auth/confirm-email",
            new { userId = query["userId"].ToString(), token = query["token"].ToString() });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task<Guid> ResolveAccountIdAsync(string connectionString, string email)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new DavetiyeDbContext(options);
        return await (from account in db.Accounts
                      join user in db.Users on account.IdentityUserId equals user.Id
                      where user.NormalizedEmail == email.ToUpperInvariant()
                      select account.Id).SingleAsync();
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, "tools", "Davetiye.DatabaseMigrator", "bin", "Debug", "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(assembly), $"Migrator assembly was not built: {assembly}");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add(assembly);
        start.Environment["Database__ConnectionString"] = testConnectionString;
        start.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start database migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Davetiye.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Kind, IReadOnlyDictionary<string, string> Data)> Sent { get; } = [];
        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
        {
            Sent.Add((toEmail, kind, data));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingMediaIntentService : ICreatorMediaIntentService
    {
        public List<CreateCreatorMediaIntentCommand> Commands { get; } = [];
        public string? ClientIpAddress { get; private set; }
        public CreatorMediaIntentResult Result { get; }
        public CreatorMediaIntentOutcome Outcome { get; set; }

        public CapturingMediaIntentService()
        {
            Result = new CreatorMediaIntentResult(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), false);
            Outcome = CreatorMediaIntentOutcome.Created(Result, new MediaUploadCapability(
                new Uri("https://upload.example.test/one-time-capability"), DateTimeOffset.UtcNow.AddMinutes(5),
                new Dictionary<string, string> { ["X-Media-Capability"] = "signed-capability" }));
        }

        public Task<CreatorMediaIntentOutcome> CreateAsync(CreateCreatorMediaIntentCommand command, string clientIpAddress, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            ClientIpAddress = clientIpAddress;
            return Task.FromResult(Outcome);
        }
    }

    private sealed class MediaApiFactory(string databaseConnectionString, CapturingEmailSender emailSender,
        CapturingMediaIntentService mediaService) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = databaseConnectionString,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(emailSender);
                services.AddSingleton<ICreatorMediaIntentService>(mediaService);
            });
        }
    }
}
