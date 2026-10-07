using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminOverviewEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Overview_returns_only_zeroed_aggregate_counts_for_an_empty_database()
    {
        await using var factory = new AdminOverviewApiFactory(connectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAdminOverviewService>();

        var overview = await reader.GetAsync(CancellationToken.None);

        Assert.Equal(0, overview.Accounts.Total);
        Assert.Equal(0, overview.Accounts.Banned);
        Assert.Equal(0, overview.Invitations.Draft);
        Assert.Equal(0, overview.Invitations.Deleted);
        Assert.Equal(4, overview.Plans.Total);
        Assert.Equal(4, overview.Plans.Active);
        Assert.Equal(0, overview.Plans.Inactive);
        Assert.Equal(0, overview.Grants.Total);
        Assert.Equal(0, overview.Payments.Pending);
        Assert.Equal(0, overview.Storage.Assets);
        Assert.Equal(0, overview.Storage.VerifiedBytes);
        Assert.Equal("Healthy", overview.Health.Api);
        Assert.Equal("Healthy", overview.Health.Database);
    }

    [Fact]
    public async Task Overview_rejects_anonymous_and_first_factor_only_requests()
    {
        await using var factory = new AdminOverviewApiFactory(connectionString);
        using var anonymousClient = factory.CreateClient();

        var anonymousResponse = await anonymousClient.GetAsync("/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        const string email = "p9-admin-overview@example.test";
        const string password = "TestPassw0rd1";
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var runner = new AdminBootstrapRunner(userManager, dbContext);
            Assert.Equal(AdminBootstrapOutcome.Created,
                await runner.RunAsync(email, password, CancellationToken.None));
        }

        var loginResponse = await anonymousClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var firstFactorResponse = await anonymousClient.GetAsync("/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, firstFactorResponse.StatusCode);
    }

    [Fact]
    public async Task Mfa_complete_admin_receives_no_store_aggregate_only_overview()
    {
        await using var factory = new AdminOverviewApiFactory(connectionString);
        using var client = factory.CreateClient();
        const string email = "p9-admin-overview-mfa@example.test";
        const string password = "TestPassw0rd1";

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var runner = new AdminBootstrapRunner(userManager, dbContext);
            Assert.Equal(AdminBootstrapOutcome.Created,
                await runner.RunAsync(email, password, CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);

        var enrollCsrf = await GetCsrfTokenAsync(client);
        using var enrollResponse = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollCsrf);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);
        using var enrollJson = JsonDocument.Parse(await enrollResponse.Content.ReadAsStringAsync());
        var sharedKey = enrollJson.RootElement.GetProperty("sharedKey").GetString()!;

        var verifyCsrf = await GetCsrfTokenAsync(client);
        using var verifyResponse = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/verify",
            new { code = GenerateTotpCode(sharedKey) }, verifyCsrf);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var completeResponse = await client.PostAsJsonAsync("/api/v1/admin/mfa/login/complete",
            new { code = GenerateTotpCode(sharedKey), isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        using var response = await client.GetAsync("/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(4, root.GetProperty("plans").GetProperty("total").GetInt64());
        Assert.Equal("Healthy", root.GetProperty("health").GetProperty("api").GetString());
        Assert.Equal("Healthy", root.GetProperty("health").GetProperty("database").GetString());
        Assert.False(root.TryGetProperty("email", out _));
        Assert.False(root.TryGetProperty("identityUserId", out _));
        Assert.False(root.TryGetProperty("accountId", out _));
        Assert.False(root.TryGetProperty("invitationId", out _));
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        var csrf = await GetCsrfTokenAsync(client);
        using var response = await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var timestepBytes = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(timestepBytes);
        var hash = HMACSHA1.HashData(key, timestepBytes);
        var offset = hash[^1] & 0xf;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var character in base32.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) continue;
            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;
            if (bitCount < 8) continue;
            output.Add((byte)((bitBuffer >> (bitCount - 8)) & 0xff));
            bitCount -= 8;
        }
        return output.ToArray();
    }

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var migratorAssembly = Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin", TestBuildConfiguration.Name, "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(migratorAssembly), $"Migrator assembly was not built: {migratorAssembly}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(migratorAssembly);
        startInfo.Environment["Database__ConnectionString"] = testConnectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the database migrator process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0,
            $"Migrator failed.{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class AdminOverviewApiFactory(string databaseConnectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = databaseConnectionString,
                    ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                    ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                }));
        }
    }
}
