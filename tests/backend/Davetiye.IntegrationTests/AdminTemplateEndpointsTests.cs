using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminTemplateEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Admin_can_edit_only_bounded_template_fields_and_hide_without_breaking_pinned_templates()
    {
        await using var factory = new AdminTemplateApiFactory(connectionString);
        using var admin = factory.CreateClient();
        const string email = "p9-template-admin@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        await CompleteAdminMfaAsync(admin, email, password);

        using var listResponse = await admin.GetAsync("/api/v1/admin/templates");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var item = listJson.RootElement.EnumerateArray().Single(row => row.GetProperty("key").GetString() == "zamansiz-dugun");
        Assert.Equal(new[] { "description", "id", "isActive", "key", "name", "revision" },
            item.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var id = item.GetProperty("id").GetGuid();
        var revision = item.GetProperty("revision").GetInt64();

        using var missingCsrf = await admin.PutAsJsonAsync($"/api/v1/admin/templates/{id:D}",
            new { expectedRevision = revision, name = "Must not persist", description = (string?)null, isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var csrf = await GetCsrfTokenAsync(admin);
        using var updateResponse = await SendWithCsrfAsync(admin, HttpMethod.Put,
            $"/api/v1/admin/templates/{id:D}",
            new { expectedRevision = revision, name = "  Updated name  ", description = "  Updated description  ", isActive = false },
            csrf);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.True(updateResponse.Headers.CacheControl?.NoStore);
        using var updatedJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        var updated = updatedJson.RootElement;
        Assert.Equal("Updated name", updated.GetProperty("name").GetString());
        Assert.Equal("Updated description", updated.GetProperty("description").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal(revision + 1, updated.GetProperty("revision").GetInt64());

        using var staleResponse = await SendWithCsrfAsync(admin, HttpMethod.Put,
            $"/api/v1/admin/templates/{id:D}",
            new { expectedRevision = revision, name = "stale", description = (string?)null, isActive = true },
            await GetCsrfTokenAsync(admin));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var publicCatalogResponse = await admin.GetAsync("/api/v1/templates");
        using var publicCatalog = JsonDocument.Parse(await publicCatalogResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(publicCatalog.RootElement.EnumerateArray(), template =>
            template.GetProperty("key").GetString() == "zamansiz-dugun");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var hidden = await db.TemplateDefinitions.SingleAsync(template => template.Id == id);
            Assert.Equal("Düğün", hidden.Category);
            Assert.False(hidden.IsPremium);
            Assert.Equal("/template-previews/zamansiz-dugun.jpg", hidden.PreviewImageUrl);
            Assert.Equal(2, hidden.CurrentRendererVersion);
            Assert.Equal(1, await db.AdminAuditRecords.CountAsync(record => record.SubjectId == id));
            var audit = await db.AdminAuditRecords.SingleAsync(record => record.SubjectId == id);
            Assert.Equal("TemplateMetadataUpdated", audit.EventType);

            var catalog = scope.ServiceProvider.GetRequiredService<ITemplateCatalogService>();
            var pinned = await catalog.ResolvePinnedAsync("zamansiz-dugun", CancellationToken.None);
            Assert.NotNull(pinned);
            Assert.Equal("Updated description", pinned.Description);
            var selector = scope.ServiceProvider.GetRequiredService<ITemplateSelectionResolver>();
            Assert.Null(await selector.ResolveActiveAsync("zamansiz-dugun", CancellationToken.None));
        }
    }

    [Fact]
    public async Task Admin_template_endpoints_require_mfa_and_updates_require_antiforgery()
    {
        await using var factory = new AdminTemplateApiFactory(connectionString);
        using var anonymous = factory.CreateClient();
        using var anonymousRead = await anonymous.GetAsync("/api/v1/admin/templates");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);

        const string email = "p9-template-first-factor@example.test";
        const string password = "TestPassw0rd1";
        await CreateAdminAsync(factory, email, password);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var firstFactorRead = await anonymous.GetAsync("/api/v1/admin/templates");
        Assert.Equal(HttpStatusCode.Forbidden, firstFactorRead.StatusCode);
    }

    [Fact]
    public async Task Concurrent_admin_updates_with_one_revision_commit_once_and_audit_once()
    {
        await using var factory = new AdminTemplateApiFactory(connectionString);
        const string email = "p9-template-concurrency-admin@example.test";
        const string password = "TestPassw0rd1";
        Guid actorId;
        Guid templateId;
        long revision;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(email, password, CancellationToken.None));
            actorId = (await userManager.FindByEmailAsync(email))!.Id;
            var template = await db.TemplateDefinitions.SingleAsync(item => item.Key == "zamansiz-dugun");
            templateId = template.Id;
            revision = template.Revision;
        }

        async Task<AdminTemplateUpdateOutcome> UpdateAsync(string name)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAdminTemplateService>();
            return (await service.UpdateAsync(actorId, templateId,
                new(revision, name, null, true), CancellationToken.None)).Outcome;
        }

        var outcomes = await Task.WhenAll(UpdateAsync("Concurrent A"), UpdateAsync("Concurrent B"));
        Assert.Equal(1, outcomes.Count(outcome => outcome == AdminTemplateUpdateOutcome.Succeeded));
        Assert.Equal(1, outcomes.Count(outcome => outcome == AdminTemplateUpdateOutcome.Conflict));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(1, await verifyDb.AdminAuditRecords.CountAsync(record =>
            record.SubjectId == templateId && record.EventType == "TemplateMetadataUpdated"));
    }

    private static async Task CreateAdminAsync(AdminTemplateApiFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(AdminBootstrapOutcome.Created,
            await new AdminBootstrapRunner(userManager, db).RunAsync(email, password, CancellationToken.None));
    }

    private static async Task CompleteAdminMfaAsync(HttpClient client, string email, string password)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        var token = await GetCsrfTokenAsync(client);
        using var enroll = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/v1/admin/mfa/enroll", new { }, token);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        using var body = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var sharedKey = body.RootElement.GetProperty("sharedKey").GetString()!;
        using var verify = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/v1/admin/mfa/verify",
            new { code = GenerateTotpCode(sharedKey) }, await GetCsrfTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        using var logout = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/v1/auth/logout", new { }, await GetCsrfTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
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

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client, HttpMethod method, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
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
        var code = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) |
                   ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (code % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var assembly = Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin", TestBuildConfiguration.Name, "net10.0", "Davetiye.DatabaseMigrator.dll");
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

    private sealed class AdminTemplateApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                ["AuthCookie:SecurityStampValidationIntervalSeconds"] = "900"
            }));
        }
    }
}
