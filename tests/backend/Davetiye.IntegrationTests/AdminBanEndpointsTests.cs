using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
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
public sealed class AdminBanEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mfa_admin_ban_revokes_target_session_denies_login_and_writes_minimal_audit()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        using var admin = factory.CreateClient();
        using var target = factory.CreateClient();
        const string adminEmail = "p9-ban-admin@example.test";
        const string targetEmail = "p9-ban-target@example.test";
        const string password = "TestPassw0rd1";
        Guid targetAccountId;
        Guid adminUserId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(adminEmail, password, CancellationToken.None));
            adminUserId = (await userManager.FindByEmailAsync(adminEmail))!.Id;

            var targetUser = new ApplicationUser
            {
                UserName = targetEmail,
                Email = targetEmail,
                EmailConfirmed = true
            };
            Assert.True((await userManager.CreateAsync(targetUser, password)).Succeeded);
            targetAccountId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(targetAccountId, targetUser.Id, AccountType.Individual, "Ban test", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await target.PostAsJsonAsync("/api/v1/auth/login", new { email = targetEmail, password })).StatusCode);
        using (var targetSession = await target.GetAsync("/api/v1/auth/session"))
        {
            Assert.Equal(HttpStatusCode.OK, targetSession.StatusCode);
            using var json = JsonDocument.Parse(await targetSession.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        }

        await CompleteAdminMfaAsync(admin, adminEmail, password);
        var token = await GetCsrfTokenAsync(admin);
        using var banResponse = await PostWithCsrfAsync(admin,
            $"/api/v1/admin/accounts/{targetAccountId:D}/ban",
            new { reason = "Policy violation", internalNote = "Reviewed by support" }, token);
        Assert.Equal(HttpStatusCode.NoContent, banResponse.StatusCode);
        Assert.True(banResponse.Headers.CacheControl?.NoStore);

        using (var targetSession = await target.GetAsync("/api/v1/auth/session"))
        {
            using var json = JsonDocument.Parse(await targetSession.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
        }

        using var loginAfterBan = await target.PostAsJsonAsync("/api/v1/auth/login", new { email = targetEmail, password });
        Assert.Equal(HttpStatusCode.Forbidden, loginAfterBan.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        var ban = await verifyDb.BanRecords.SingleAsync(record => record.AccountId == targetAccountId);
        Assert.Equal("Policy violation", ban.Reason);
        Assert.Equal("Reviewed by support", ban.InternalNote);
        Assert.Equal(adminUserId, ban.BannedByActorId);
        var audit = await verifyDb.AdminAuditRecords.SingleAsync(record => record.SubjectId == targetAccountId);
        Assert.Equal(adminUserId, audit.ActorId);
        Assert.Equal("AccountBanned", audit.EventType);
    }

    [Fact]
    public async Task Repeated_ban_is_conflict_and_does_not_duplicate_audit()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        using var admin = factory.CreateClient();
        const string adminEmail = "p9-ban-duplicate-admin@example.test";
        const string password = "TestPassw0rd1";
        Guid targetAccountId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(adminEmail, password, CancellationToken.None));
            var targetUser = new ApplicationUser { UserName = "p9-ban-duplicate-target@example.test", Email = "p9-ban-duplicate-target@example.test", EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(targetUser, password)).Succeeded);
            targetAccountId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(targetAccountId, targetUser.Id, AccountType.Individual, "Ban duplicate", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await CompleteAdminMfaAsync(admin, adminEmail, password);
        using var missingCsrf = await admin.PostAsJsonAsync($"/api/v1/admin/accounts/{targetAccountId:D}/ban", new { reason = "No token" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var firstToken = await GetCsrfTokenAsync(admin);
        using var first = await PostWithCsrfAsync(admin, $"/api/v1/admin/accounts/{targetAccountId:D}/ban", new { reason = "First" }, firstToken);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var secondToken = await GetCsrfTokenAsync(admin);
        using var second = await PostWithCsrfAsync(admin, $"/api/v1/admin/accounts/{targetAccountId:D}/ban", new { reason = "Second" }, secondToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(1, await dbContext.BanRecords.CountAsync(record => record.AccountId == targetAccountId));
        Assert.Equal(1, await dbContext.AdminAuditRecords.CountAsync(record => record.SubjectId == targetAccountId));
    }

    [Fact]
    public async Task Concurrent_bans_serialize_to_one_success_and_one_conflict()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        const string adminEmail = "p9-ban-concurrent-admin@example.test";
        const string password = "TestPassw0rd1";
        Guid actorId;
        Guid targetAccountId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(adminEmail, password, CancellationToken.None));
            actorId = (await userManager.FindByEmailAsync(adminEmail))!.Id;
            var targetUser = new ApplicationUser { UserName = "p9-ban-concurrent-target@example.test", Email = "p9-ban-concurrent-target@example.test", EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(targetUser, password)).Succeeded);
            targetAccountId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(targetAccountId, targetUser.Id, AccountType.Individual, "Concurrent ban", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        async Task<Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminBanOutcome> BanAsync(string reason)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<Davetiye.Application.Modules.IdentityAndAccounts.Contracts.IAdminBanService>();
            return (await service.BanAsync(actorId, targetAccountId,
                new AdminBanRequest { Reason = reason }, CancellationToken.None)).Outcome;
        }

        var outcomes = await Task.WhenAll(BanAsync("Concurrent A"), BanAsync("Concurrent B"));
        Assert.Equal(1, outcomes.Count(outcome => outcome == Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminBanOutcome.Succeeded));
        Assert.Equal(1, outcomes.Count(outcome => outcome == Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminBanOutcome.AlreadyBanned));
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(1, await verifyDb.BanRecords.CountAsync(record => record.AccountId == targetAccountId));
        Assert.Equal(1, await verifyDb.AdminAuditRecords.CountAsync(record => record.SubjectId == targetAccountId));
    }

    [Fact]
    public async Task Ban_endpoint_requires_mfa_complete_and_antiforgery()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var anonymous = await client.PostAsJsonAsync("/api/v1/admin/accounts/00000000-0000-0000-0000-000000000001/ban", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        const string email = "p9-ban-first-factor@example.test";
        const string password = "TestPassw0rd1";
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(email, password, CancellationToken.None));
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var firstFactor = await client.PostAsJsonAsync("/api/v1/admin/accounts/00000000-0000-0000-0000-000000000001/ban", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, firstFactor.StatusCode);
    }

    [Fact]
    public async Task Unban_requires_mfa_and_csrf_preserves_history_rotates_stamp_and_restores_login()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        using var admin = factory.CreateClient();
        using var target = factory.CreateClient();
        const string adminEmail = "p9-unban-admin@example.test";
        const string targetEmail = "p9-unban-target@example.test";
        const string password = "TestPassw0rd1";
        Guid targetAccountId;
        string originalStamp;
        Guid adminUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(adminEmail, password, CancellationToken.None));
            adminUserId = (await userManager.FindByEmailAsync(adminEmail))!.Id;

            var targetUser = new ApplicationUser
            {
                UserName = targetEmail,
                Email = targetEmail,
                EmailConfirmed = true
            };
            Assert.True((await userManager.CreateAsync(targetUser, password)).Succeeded);
            originalStamp = targetUser.SecurityStamp!;
            targetAccountId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(targetAccountId, targetUser.Id, AccountType.Individual, "Unban test", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        using (var anonymousUnknown = await admin.PostAsJsonAsync(
            "/api/v1/admin/accounts/00000000-0000-0000-0000-000000000001/unban", new { }))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousUnknown.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/auth/login", new { email = adminEmail, password })).StatusCode);
        using (var firstFactor = await admin.PostAsJsonAsync($"/api/v1/admin/accounts/{targetAccountId:D}/unban", new { }))
            Assert.Equal(HttpStatusCode.Forbidden, firstFactor.StatusCode);

        await CompleteAdminMfaAsync(admin, adminEmail, password);
        var csrf = await GetCsrfTokenAsync(admin);
        using (var missingAccount = await PostWithCsrfAsync(admin,
            "/api/v1/admin/accounts/00000000-0000-0000-0000-000000000001/unban", new { }, csrf))
            Assert.Equal(HttpStatusCode.NotFound, missingAccount.StatusCode);
        using (var noActiveBan = await PostWithCsrfAsync(admin, $"/api/v1/admin/accounts/{targetAccountId:D}/unban", new { }, csrf))
            Assert.Equal(HttpStatusCode.Conflict, noActiveBan.StatusCode);

        string preBanAuthCookie;
        using (var targetLogin = await target.PostAsJsonAsync("/api/v1/auth/login", new { email = targetEmail, password }))
        {
            Assert.Equal(HttpStatusCode.OK, targetLogin.StatusCode);
            Assert.True(targetLogin.Headers.TryGetValues("Set-Cookie", out var cookies));
            preBanAuthCookie = cookies!
                .Select(value => value.Split(';', 2)[0])
                .Single(value => value.StartsWith("davetiye-auth-dev=", StringComparison.Ordinal));
        }
        var banToken = await GetCsrfTokenAsync(admin);
        using (var ban = await PostWithCsrfAsync(admin, $"/api/v1/admin/accounts/{targetAccountId:D}/ban", new { reason = "Temporary restriction" }, banToken))
            Assert.Equal(HttpStatusCode.NoContent, ban.StatusCode);

        using (var missingCsrf = await admin.PostAsJsonAsync($"/api/v1/admin/accounts/{targetAccountId:D}/unban", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var unbanToken = await GetCsrfTokenAsync(admin);
        using (var unban = await PostWithCsrfAsync(admin, $"/api/v1/admin/accounts/{targetAccountId:D}/unban", new { }, unbanToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, unban.StatusCode);
            Assert.True(unban.Headers.CacheControl?.NoStore);
        }

        // This copied cookie is intentionally never sent while the ban is active. It must remain
        // revoked after unban even though the test configures a 900-second stamp-validation interval.
        using (var replayClient = factory.CreateClient())
        using (var replay = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session"))
        {
            replay.Headers.Add("Cookie", preBanAuthCookie);
            using var replayResponse = await replayClient.SendAsync(replay);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            using var replayJson = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
            Assert.False(replayJson.RootElement.GetProperty("authenticated").GetBoolean());
        }

        using (var freshLogin = await target.PostAsJsonAsync("/api/v1/auth/login", new { email = targetEmail, password }))
            Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
        using (var targetSession = await target.GetAsync("/api/v1/auth/session"))
        {
            using var json = JsonDocument.Parse(await targetSession.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        var banRecord = await verifyDb.BanRecords.SingleAsync(record => record.AccountId == targetAccountId);
        Assert.NotNull(banRecord.RevokedAt);
        Assert.Equal("Temporary restriction", banRecord.Reason);
        var audit = await verifyDb.AdminAuditRecords
            .Where(record => record.SubjectId == targetAccountId)
            .OrderBy(record => record.EventType)
            .ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, record => record.ActorId == adminUserId && record.EventType == "AccountBanned");
        Assert.Contains(audit, record => record.ActorId == adminUserId && record.EventType == "AccountUnbanned");
        var changedStamp = (await verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(targetEmail))!.SecurityStamp;
        Assert.NotEqual(originalStamp, changedStamp);
    }

    [Fact]
    public async Task Concurrent_unban_commands_revoke_one_record_and_write_one_unban_audit()
    {
        await using var factory = new AdminBanApiFactory(connectionString);
        const string adminEmail = "p9-unban-race-admin@example.test";
        const string targetEmail = "p9-unban-race-target@example.test";
        const string password = "TestPassw0rd1";
        Guid actorId;
        Guid targetAccountId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(userManager, db).RunAsync(adminEmail, password, CancellationToken.None));
            actorId = (await userManager.FindByEmailAsync(adminEmail))!.Id;
            var targetUser = new ApplicationUser { UserName = targetEmail, Email = targetEmail, EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(targetUser, password)).Succeeded);
            targetAccountId = Guid.NewGuid();
            db.Accounts.Add(Account.Create(targetAccountId, targetUser.Id, AccountType.Individual, "Unban race", DateTimeOffset.UtcNow));
            db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), targetAccountId, "Temporary restriction", DateTimeOffset.UtcNow, actorId));
            await db.SaveChangesAsync();
        }

        async Task<Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminUnbanOutcome> UnbanAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<Davetiye.Application.Modules.IdentityAndAccounts.Contracts.IAdminBanService>();
            return (await service.UnbanAsync(actorId, targetAccountId, CancellationToken.None)).Outcome;
        }

        var outcomes = await Task.WhenAll(UnbanAsync(), UnbanAsync());
        Assert.Equal(1, outcomes.Count(outcome => outcome == Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminUnbanOutcome.Succeeded));
        Assert.Equal(1, outcomes.Count(outcome => outcome == Davetiye.Application.Modules.IdentityAndAccounts.Contracts.AdminUnbanOutcome.NoActiveBan));

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        Assert.Equal(1, await verifyDb.BanRecords.CountAsync(record => record.AccountId == targetAccountId));
        Assert.Equal(1, await verifyDb.BanRecords.CountAsync(record => record.AccountId == targetAccountId && record.RevokedAt != null));
        Assert.Equal(1, await verifyDb.AdminAuditRecords.CountAsync(record => record.SubjectId == targetAccountId && record.EventType == "AccountUnbanned"));
    }

    private static async Task CompleteAdminMfaAsync(HttpClient client, string email, string password)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        var enrollToken = await GetCsrfTokenAsync(client);
        using var enroll = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollToken);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        using var json = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var sharedKey = json.RootElement.GetProperty("sharedKey").GetString()!;
        var verifyToken = await GetCsrfTokenAsync(client);
        using var verify = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/verify", new { code = GenerateTotpCode(sharedKey) }, verifyToken);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var logoutToken = await GetCsrfTokenAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, logoutToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var complete = await client.PostAsJsonAsync("/api/v1/admin/mfa/login/complete", new { code = GenerateTotpCode(sharedKey), isRecoveryCode = false });
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
        var code = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
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
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class AdminBanApiFactory(string connection) : WebApplicationFactory<Program>
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
