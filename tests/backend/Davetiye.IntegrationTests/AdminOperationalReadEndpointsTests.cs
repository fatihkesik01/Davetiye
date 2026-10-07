using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Payments;
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
public sealed class AdminOperationalReadEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Payments_and_audit_require_mfa_complete_and_are_not_cacheable()
    {
        await using var factory = new AdminReadApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var anonymousPayment = await client.GetAsync("/api/v1/admin/payments");
        using var anonymousAudit = await client.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPayment.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAudit.StatusCode);

        const string email = "p9-admin-ops-first-factor@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);

        using var firstFactorPayment = await client.GetAsync("/api/v1/admin/payments");
        using var firstFactorAudit = await client.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.Forbidden, firstFactorPayment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, firstFactorAudit.StatusCode);

        await CompleteAdminMfaAsync(client, email, password);
        using var paymentResponse = await client.GetAsync("/api/v1/admin/payments");
        using var auditResponse = await client.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        Assert.True(paymentResponse.Headers.CacheControl?.NoStore);
        Assert.True(auditResponse.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Payment_and_audit_pages_have_stable_descending_order_and_minimized_allowlisted_fields()
    {
        await using var factory = new AdminReadApiFactory(connectionString);
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var paymentIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var auditIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            for (var i = 0; i < paymentIds.Length; i++)
            {
                var attempt = PaymentAttempt.Create(
                    paymentIds[i], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "premium",
                    1499m, "TRY", $"p9-admin-idem-key-{i:000}", $"local-p9-ref-{i:000}", now);
                attempt.SetCheckout("https://checkout.example.test/provider-secret-url", $"provider-checkout-secret-{i}", now);
                db.PaymentAttempts.Add(attempt);
                db.AdminAuditRecords.Add(AdminAuditRecord.Create(
                    auditIds[i], actorId, now, $"P9TestEvent{i}", subjectId));
            }
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        const string adminEmail = "p9-admin-ops-list@example.test";
        const string adminPassword = "TestPassw0rd1";
        await CreateAdminAsync(factory, adminEmail, adminPassword);
        await CompleteAdminMfaAsync(client, adminEmail, adminPassword);

        using var paymentPageOne = await client.GetAsync("/api/v1/admin/payments?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, paymentPageOne.StatusCode);
        using var paymentJsonOne = JsonDocument.Parse(await paymentPageOne.Content.ReadAsStringAsync());
        var paymentRootOne = paymentJsonOne.RootElement;
        Assert.Equal(3, paymentRootOne.GetProperty("totalCount").GetInt64());
        Assert.Equal(1, paymentRootOne.GetProperty("page").GetInt32());
        Assert.Equal(2, paymentRootOne.GetProperty("pageSize").GetInt32());
        var paymentItemsOne = paymentRootOne.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, paymentItemsOne.Length);
        Assert.Equal(paymentIds.OrderDescending().Take(2), paymentItemsOne.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(new[]
        {
            "id", "reference", "status", "planKey", "amount", "currency", "createdAtUtc", "updatedAtUtc",
            "reversalKind", "reversedAtUtc"
        }.Order(StringComparer.Ordinal), paymentItemsOne[0].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var paymentBody = paymentJsonOne.RootElement.GetRawText();
        Assert.DoesNotContain("accountId", paymentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invitationId", paymentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planId", paymentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkoutUrl", paymentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerCheckoutId", paymentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-checkout-secret-", paymentBody, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret-url", paymentBody, StringComparison.Ordinal);
        Assert.DoesNotContain("idempotencyKey", paymentBody, StringComparison.OrdinalIgnoreCase);

        using var paymentPageTwo = await client.GetAsync("/api/v1/admin/payments?page=2&pageSize=2");
        using var paymentJsonTwo = JsonDocument.Parse(await paymentPageTwo.Content.ReadAsStringAsync());
        var paymentItemsTwo = paymentJsonTwo.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(paymentItemsTwo);
        Assert.Equal(paymentIds.OrderDescending().Last(), paymentItemsTwo[0].GetProperty("id").GetGuid());

        using var auditResponse = await client.GetAsync("/api/v1/admin/audit?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        using var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        var auditRoot = auditJson.RootElement;
        Assert.Equal(3, auditRoot.GetProperty("totalCount").GetInt64());
        var auditItems = auditRoot.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(auditIds.OrderDescending().Take(2), auditItems.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { "id", "actorId", "subjectId", "occurredAtUtc", "eventType" }.Order(StringComparer.Ordinal),
            auditItems[0].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var auditBody = auditJson.RootElement.GetRawText();
        Assert.DoesNotContain("internalNote", auditBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", auditBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reason", auditBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=21474837&pageSize=100")]
    [InlineData("?page=2147483648")]
    public async Task Both_lists_reject_invalid_and_overflowing_pagination(string query)
    {
        await using var factory = new AdminReadApiFactory(connectionString);
        using var client = factory.CreateClient();
        const string adminEmail = "p9-admin-ops-pages@example.test";
        const string adminPassword = "TestPassw0rd1";
        await CreateAdminAsync(factory, adminEmail, adminPassword);
        await CompleteAdminMfaAsync(client, adminEmail, adminPassword);

        foreach (var route in new[] { "/api/v1/admin/payments", "/api/v1/admin/audit" })
        {
            using var response = await client.GetAsync(route + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
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
        {
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class AdminReadApiFactory(string databaseConnectionString) : WebApplicationFactory<Program>
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
                    ["AdminBootstrap:Email"] = "p9-admin-ops-list@example.test",
                    ["AdminBootstrap:Password"] = "TestPassw0rd1"
                }));
        }
    }
}
