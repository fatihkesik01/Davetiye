using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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
public sealed class AdminBannedAccountEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Banned_account_list_requires_completed_mfa_and_is_not_cacheable()
    {
        await using var factory = new AdminBannedApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var anonymous = await client.PostAsJsonAsync("/api/v1/admin/accounts/banned/search", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(anonymous.Headers.CacheControl?.NoStore);

        const string email = "p9-admin-banned-auth@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);

        using var firstFactor = await client.PostAsJsonAsync("/api/v1/admin/accounts/banned/search", new { });
        Assert.Equal(HttpStatusCode.Forbidden, firstFactor.StatusCode);
        Assert.True(firstFactor.Headers.CacheControl?.NoStore);

        await CompleteAdminMfaAsync(client, email, password);
        using var completed = await PostSearchAsync(client, new { });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(completed.Headers.CacheControl?.NoStore);
        Assert.Equal("no-cache", completed.Headers.Pragma.Single().ToString());
    }

    [Fact]
    public async Task Banned_accounts_support_case_insensitive_email_prefix_paging_and_minimized_projection()
    {
        await using var factory = new AdminBannedApiFactory(connectionString);
        const string adminEmail = "p9-admin-banned-list@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, adminEmail, password);

        var accountIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var bannedAt = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            for (var i = 0; i < accountIds.Length; i++)
            {
                var email = $"P9.Banned{i}@Example.Test";
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                db.Accounts.Add(Account.Create(accountIds[i], user.Id,
                    i == 1 ? AccountType.Organization : AccountType.Individual,
                    $"Banned display {i}", bannedAt.AddDays(-10 + i)));
                db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), accountIds[i], $"Reason {i}",
                    bannedAt.AddMinutes(i), Guid.NewGuid(), $"SECRET_INTERNAL_NOTE_{i}"));
            }

            // Revoked history and an unbanned account must not appear in the active-ban listing.
            var oldUser = new ApplicationUser { UserName = "p9.old@example.test", Email = "p9.old@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(oldUser, password)).Succeeded);
            var oldId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(oldId, oldUser.Id, AccountType.Individual, "Revoked", bannedAt));
            var revoked = BanRecord.Create(Guid.NewGuid(), oldId, "Old reason", bannedAt, Guid.NewGuid());
            revoked.Revoke(bannedAt.AddMinutes(1));
            db.BanRecords.Add(revoked);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        await CompleteAdminMfaAsync(client, adminEmail, password);

        using var pageOne = await PostSearchAsync(client, new { emailPrefix = "p9.bAnNeD", pageSize = 2 });
        Assert.Equal(HttpStatusCode.OK, pageOne.StatusCode);
        Assert.True(pageOne.Headers.CacheControl?.NoStore);
        using var oneJson = JsonDocument.Parse(await pageOne.Content.ReadAsStringAsync());
        var one = oneJson.RootElement;
        Assert.Equal(3, one.GetProperty("totalCount").GetInt64());
        Assert.Equal(1, one.GetProperty("page").GetInt32());
        Assert.Equal(2, one.GetProperty("pageSize").GetInt32());
        var firstItems = one.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { accountIds[2], accountIds[1] }, firstItems.Select(item => item.GetProperty("accountId").GetGuid()));
        Assert.Equal(new[] { "accountId", "displayName", "accountType", "email", "createdAtUtc", "bannedAtUtc", "reason" }.Order(StringComparer.Ordinal),
            firstItems[0].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var pageBody = oneJson.RootElement.GetRawText();
        Assert.DoesNotContain("SECRET_INTERNAL_NOTE", pageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("identityUserId", pageBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invitation", pageBody, StringComparison.OrdinalIgnoreCase);

        using var pageTwo = await PostSearchAsync(client, new { emailPrefix = "P9.BANNED", page = 2, pageSize = 2 });
        using var twoJson = JsonDocument.Parse(await pageTwo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, pageTwo.StatusCode);
        Assert.Equal(new[] { accountIds[0] }, twoJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("accountId").GetGuid()));
        Assert.Equal("P9.Banned0@Example.Test", twoJson.RootElement.GetProperty("items")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task OpenApi_documents_banned_account_query_parameters_and_page_response()
    {
        await using var factory = new AdminBannedApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/admin/accounts/banned/search")
            .GetProperty("post");

        var requestBodySchema = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.Contains("AdminBannedAccountSearchRequest", requestBodySchema.GetProperty("$ref").GetString(), StringComparison.Ordinal);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var components = schemas.GetProperty("AdminBannedAccountSearchRequest");
        var searchProperties = components.GetProperty("properties");
        Assert.Equal(new[] { "emailPrefix", "page", "pageSize" }, searchProperties
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(components.TryGetProperty("required", out _),
            "Search filters and pagination may be omitted so server defaults remain usable.");
        var itemSchema = schemas.GetProperty("AdminBannedAccountPage").GetProperty("properties")
            .GetProperty("items").GetProperty("items").GetProperty("$ref").GetString();
        Assert.NotNull(itemSchema);
        var itemName = itemSchema.Split('/').Last();
        var accountType = schemas.GetProperty(itemName).GetProperty("properties").GetProperty("accountType").GetProperty("$ref").GetString();
        Assert.NotNull(accountType);
        var accountTypeName = accountType.Split('/').Last();
        Assert.Equal(new[] { "individual", "organization" }, schemas.GetProperty(accountTypeName).GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(operation.TryGetProperty("parameters", out _), "Search inputs must not appear in the URL query string.");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out var success));
        Assert.True(success.GetProperty("content").TryGetProperty("application/json", out var json));
        Assert.True(json.GetProperty("schema").TryGetProperty("$ref", out var pageSchema));
        Assert.Contains("AdminBannedAccountPage", pageSchema.GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"page\":0}")]
    [InlineData("{\"pageSize\":101}")]
    [InlineData("{\"page\":21474837,\"pageSize\":100}")]
    [InlineData("{\"page\":\"not-a-number\"}")]
    [InlineData("{\"emailPrefix\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")]
    public async Task Invalid_or_oversized_search_body_is_rejected_with_no_store(string body)
    {
        await using var factory = new AdminBannedApiFactory(connectionString);
        const string email = "p9-admin-banned-invalid@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        using var client = factory.CreateClient();
        await CompleteAdminMfaAsync(client, email, password);

        var token = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/accounts/banned/search")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Search_requires_antiforgery_token()
    {
        await using var factory = new AdminBannedApiFactory(connectionString);
        const string email = "p9-admin-banned-csrf@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        using var client = factory.CreateClient();
        await CompleteAdminMfaAsync(client, email, password);

        using var response = await client.PostAsJsonAsync("/api/v1/admin/accounts/banned/search", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static async Task CreateAdminAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(AdminBootstrapOutcome.Created,
            await new AdminBootstrapRunner(userManager, db).RunAsync(email, password, CancellationToken.None));
    }

    private static async Task CompleteAdminMfaAsync(HttpClient client, string email, string password)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        var enrollToken = await GetCsrfTokenAsync(client);
        using var enroll = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollToken);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        using var json = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var sharedKey = json.RootElement.GetProperty("sharedKey").GetString()!;
        var verifyToken = await GetCsrfTokenAsync(client);
        using var verify = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/verify",
            new { code = GenerateTotpCode(sharedKey) }, verifyToken);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var logoutToken = await GetCsrfTokenAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, logoutToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var complete = await client.PostAsJsonAsync("/api/v1/admin/mfa/login/complete",
            new { code = GenerateTotpCode(sharedKey), isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostSearchAsync(HttpClient client, object body)
    {
        var token = await GetCsrfTokenAsync(client);
        return await PostWithCsrfAsync(client, "/api/v1/admin/accounts/banned/search", body, token);
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var key = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in base32Secret.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) continue;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits < 8) continue;
            key.Add((byte)((buffer >> (bits - 8)) & 0xff));
            bits -= 8;
        }
        var timestep = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(timestep);
        var hash = HMACSHA1.HashData(key.ToArray(), timestep);
        var offset = hash[^1] & 0xf;
        var code = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (code % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, "tools", "Davetiye.DatabaseMigrator", "bin", "Debug", "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(assembly), $"Migrator assembly was not built: {assembly}");
        var info = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add(assembly);
        info.Environment["Database__ConnectionString"] = testConnectionString;
        info.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start database migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator failed.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class AdminBannedApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test"
            }));
        }
    }
}
