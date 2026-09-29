using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// M6b's Google OAuth / Admin bootstrap / TOTP-MFA release gate. Google's own OAuth server obviously
/// cannot be called in a test: every test here only stubs the external-provider BOUNDARY (by directly
/// establishing the same <c>IdentityConstants.ExternalScheme</c> cookie the real handler would set
/// after a genuine exchange, via <see cref="GoogleLoginSimulationStartupFilter"/>), then exercises
/// the real, unmodified production <see cref="IGoogleSignInService"/> code from that point on.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AdminMfaAndGoogleEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string AllowedOrigin = "https://allowed.example.test";
    private const string ValidPassword = "TestPassw0rd1";

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Google_enabled_in_production_without_a_verified_https_callback_fails_startup()
    {
        var factory = CreateFactory(
            environmentName: "Production",
            extraConfig: new Dictionary<string, string?>
            {
                ["GoogleAuth:Enabled"] = "true",
                ["GoogleAuth:ClientId"] = "test-client-id",
                ["GoogleAuth:ClientSecret"] = "test-client-secret",
                ["GoogleAuth:CallbackBaseUrl"] = "https://203.0.113.10/api/v1/auth/google/oauth-callback",
            });

        // WebApplicationFactory builds/starts the real host lazily on first server access.
        // Program.cs resolves IOptions<GoogleAuthOptions> immediately after Build(), so the
        // fail-closed GoogleAuthOptionsValidator throws here, before app.Run() and before any
        // request could ever be served (docs/THREAT_MODEL.md §6/§12 gate 3).
        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("GoogleAuth", exception!.ToString(), StringComparison.Ordinal);

        factory.Dispose();
    }

    [Fact]
    public async Task Google_first_time_sign_in_with_a_new_email_creates_a_new_account()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        var sub = Guid.NewGuid().ToString();

        var simulateResponse = await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}&name=Test+User",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri(
            "/api/v1/auth/google/complete?returnUrl=%2Fcreator%2Fdashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);
        Assert.Equal(
            "https://davetiye.example.test/creator/dashboard",
            completeResponse.Headers.Location?.OriginalString);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.True(user.EmailConfirmed);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);
        Assert.Equal(AccountType.Individual, account.AccountType);
    }

    [Fact]
    public async Task Google_sign_in_with_an_unverified_email_is_rejected_and_does_not_create_an_account()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        var sub = Guid.NewGuid().ToString();

        var simulateResponse = await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}&emailVerified=false",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, completeResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        Assert.False(await context.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Google_existing_linked_identity_sign_in_is_rejected_for_a_banned_account()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        var sub = Guid.NewGuid().ToString();

        // First sign-in creates the Account and links the Google identity to it.
        await firstClient.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}&name=Test+User",
            UriKind.Relative));
        var firstCompleteResponse = await firstClient.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, firstCompleteResponse.StatusCode);

        await BanAccountAsync(email);

        // A fresh client/session exercising GoogleSignInService.CompleteSignInAsync's
        // ExternalLoginSignInAsync existing-linked-identity fast path (the same provider+key is
        // already linked, so this never reaches the new-account-creation branch).
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var secondSimulateResponse = await secondClient.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, secondSimulateResponse.StatusCode);

        var secondCompleteResponse = await secondClient.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, secondCompleteResponse.StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_with_an_email_matching_an_existing_unlinked_account_does_not_merge()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        var simulateResponse = await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, completeResponse.StatusCode);

        // No silent merge: the account still has no Google login attached.
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == user.Id));
    }

    [Fact]
    public async Task Admin_bootstrap_runner_grants_the_super_admin_claim_without_creating_an_account_and_is_idempotent()
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        var runner = new AdminBootstrapRunner(userManager, dbContext);
        var email = UniqueEmail();

        var firstOutcome = await runner.RunAsync(email, ValidPassword, CancellationToken.None);
        Assert.Equal(AdminBootstrapOutcome.Created, firstOutcome);

        var secondOutcome = await runner.RunAsync(email, password: null, CancellationToken.None);
        Assert.Equal(AdminBootstrapOutcome.AlreadySuperAdmin, secondOutcome);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.True(user.EmailConfirmed);
        Assert.False(await context.Accounts.AnyAsync(account => account.IdentityUserId == user.Id));

        var claims = await userManager.GetClaimsAsync(user);
        Assert.Contains(
            claims,
            claim => claim.Type == SuperAdminClaimNames.SuperAdmin && claim.Value == SuperAdminClaimNames.SuperAdminClaimValue);
    }

    [Fact]
    public async Task Admin_bootstrap_refuses_to_grant_super_admin_to_a_user_who_already_has_a_creator_account()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        // Seeds a real Creator Account the ordinary way (register + confirm), then attempts to
        // bootstrap the SAME email as Super Admin.
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        var runner = new AdminBootstrapRunner(userManager, dbContext);

        var outcome = await runner.RunAsync(email, password: null, CancellationToken.None);

        Assert.Equal(AdminBootstrapOutcome.RefusedExistingCreatorAccount, outcome);

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        var claims = await userManager.GetClaimsAsync(user!);
        Assert.DoesNotContain(
            claims,
            claim => claim.Type == SuperAdminClaimNames.SuperAdmin && claim.Value == SuperAdminClaimNames.SuperAdminClaimValue);
    }

    [Fact]
    public async Task Totp_enrollment_verification_and_two_factor_login_completion_work_end_to_end()
    {
        var mfaCompleteProbe = new MfaCompletePolicyProbeStartupFilter();
        await using var factory = CreateFactory(extraStartupFilter: mfaCompleteProbe);
        using var client = factory.CreateClient();

        var email = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, email, ValidPassword);

        // First login: MFA is not enabled yet, so this behaves exactly like a non-2FA account.
        var firstLoginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, firstLoginResponse.StatusCode);
        var firstLoginBody = await firstLoginResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("requiresTwoFactor", firstLoginBody, StringComparison.Ordinal);

        // Enroll + verify, using this same first-factor-only session (gated by "SuperAdminOnly", not
        // "MfaComplete" - there is nothing to complete yet).
        var enrollCsrf = await GetCsrfTokenAsync(client);
        var enrollResponse = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollCsrf);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);
        using var enrollDocument = JsonDocument.Parse(await enrollResponse.Content.ReadAsStringAsync());
        var sharedKey = enrollDocument.RootElement.GetProperty("sharedKey").GetString()!;

        var verifyCsrf = await GetCsrfTokenAsync(client);
        var verifyResponse = await PostWithCsrfAsync(
            client, "/api/v1/admin/mfa/verify", new { code = GenerateTotpCode(sharedKey) }, verifyCsrf);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        using var verifyDocument = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync());
        var recoveryCodes = verifyDocument.RootElement.GetProperty("recoveryCodes")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();
        Assert.NotEmpty(recoveryCodes);

        // Before completing a fresh second-factor login, this session (still only first-factor) must
        // NOT satisfy "MfaComplete" even though it does hold the Super Admin claim.
        var probeBeforeLogout = await client.GetAsync(new Uri(MfaCompletePolicyProbeStartupFilter.Path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, probeBeforeLogout.StatusCode);

        var logoutCsrf = await GetCsrfTokenAsync(client);
        var logoutResponse = await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, logoutCsrf);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Second login: MFA is now enabled, so sign-in must not complete yet.
        var secondLoginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, secondLoginResponse.StatusCode);
        var secondLoginBody = await secondLoginResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"requiresTwoFactor\":true", secondLoginBody, StringComparison.Ordinal);

        // A wrong code does not complete sign-in.
        var wrongCodeResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/admin/mfa/login/complete", UriKind.Relative),
            new { code = "000000", isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);

        var completeResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/admin/mfa/login/complete", UriKind.Relative),
            new { code = GenerateTotpCode(sharedKey), isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        // Now the session carries both the Super Admin claim and the per-session "amr=mfa" marker.
        var probeAfterCompletion = await client.GetAsync(new Uri(MfaCompletePolicyProbeStartupFilter.Path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, probeAfterCompletion.StatusCode);

        // A recovery code completes a fresh second-factor challenge too, and is single-use.
        await LogoutAsync(client);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });

        var recoveryCode = recoveryCodes[0];
        var recoveryCompleteResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/admin/mfa/login/complete", UriKind.Relative),
            new { code = recoveryCode, isRecoveryCode = true });
        Assert.Equal(HttpStatusCode.OK, recoveryCompleteResponse.StatusCode);

        await LogoutAsync(client);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        var reusedRecoveryCodeResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/admin/mfa/login/complete", UriKind.Relative),
            new { code = recoveryCode, isRecoveryCode = true });
        Assert.Equal(HttpStatusCode.BadRequest, reusedRecoveryCodeResponse.StatusCode);
    }

    [Fact]
    public async Task MfaComplete_policy_rejects_a_creator_and_a_super_admin_who_has_not_completed_a_second_factor()
    {
        var mfaCompleteProbe = new MfaCompletePolicyProbeStartupFilter();
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(extraStartupFilter: mfaCompleteProbe, emailSender: emailSender);
        using var creatorClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();

        var creatorEmail = UniqueEmail();
        await RegisterAndConfirmAsync(creatorClient, emailSender, creatorEmail, ValidPassword);
        var creatorLoginResponse = await creatorClient.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email = creatorEmail, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, creatorLoginResponse.StatusCode);

        var creatorProbe = await creatorClient.GetAsync(new Uri(MfaCompletePolicyProbeStartupFilter.Path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, creatorProbe.StatusCode);

        var adminEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, adminEmail, ValidPassword);
        var adminLoginResponse = await adminClient.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email = adminEmail, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, adminLoginResponse.StatusCode);

        // Has the Super Admin claim (authenticated, first factor only) but has never enrolled in
        // MFA, so there is no "amr=mfa" claim on this session either.
        var adminProbe = await adminClient.GetAsync(new Uri(MfaCompletePolicyProbeStartupFilter.Path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, adminProbe.StatusCode);
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        var csrf = await GetCsrfTokenAsync(client);
        await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, csrf);
    }

    private static async Task BootstrapSuperAdminAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        var runner = new AdminBootstrapRunner(userManager, dbContext);
        var outcome = await runner.RunAsync(email, password, CancellationToken.None);
        Assert.Equal(AdminBootstrapOutcome.Created, outcome);
    }

    private async Task BanAccountAsync(string email)
    {
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);

        context.BanRecords.Add(BanRecord.Create(
            Guid.NewGuid(), account.Id, "Integration test ban", DateTimeOffset.UtcNow, Guid.NewGuid()));
        await context.SaveChangesAsync();
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client, string path, object body, string csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request);
    }

    private static async Task RegisterAndConfirmAsync(
        HttpClient client, CapturingEmailSender emailSender, string email, string password)
    {
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password, displayName = "Test User" });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var confirmation = emailSender.Sent.Single(message =>
            message.Kind == EmailNotificationKinds.EmailConfirmation && message.ToEmail == email);
        var (userId, token) = ExtractUserIdAndToken(confirmation.Data["confirmationLink"]);

        var confirmResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative), new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
    }

    private static (string UserId, string Token) ExtractUserIdAndToken(string link)
    {
        var uri = new Uri(link);
        var query = QueryHelpers.ParseQuery(uri.Query);
        return (query["userId"].ToString(), query["token"].ToString());
    }

    private static string UniqueEmail() => $"m6b-test-{Guid.NewGuid():N}@example.test";

    private static Dictionary<string, string?> GoogleEnabledConfig() => new()
    {
        ["GoogleAuth:Enabled"] = "true",
        ["GoogleAuth:ClientId"] = "test-client-id",
        ["GoogleAuth:ClientSecret"] = "test-client-secret",
    };

    /// <summary>
    /// Generates a standard RFC 6238 TOTP code from a base32 (RFC 4648) authenticator secret,
    /// matching ASP.NET Core Identity's own <c>AuthenticatorTokenProvider</c>/
    /// <c>Rfc6238AuthenticationService</c> algorithm exactly (30-second step, HMAC-SHA1, dynamic
    /// truncation, 6 digits) so this test can prove a real code round-trips through the real
    /// production verification path — no custom TOTP crypto exists in production code; this exists
    /// purely as a real-authenticator-app stand-in for the test.
    /// </summary>
    private static string GenerateTotpCode(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var timestepNumber = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var timestepBytes = BitConverter.GetBytes(timestepNumber);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timestepBytes);
        }

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
        var trimmed = base32.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var character in trimmed)
        {
            var value = alphabet.IndexOf(character);
            if (value < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;

            if (bitCount >= 8)
            {
                output.Add((byte)((bitBuffer >> (bitCount - 8)) & 0xFF));
                bitCount -= 8;
            }
        }

        return output.ToArray();
    }

    private AdminTestWebApplicationFactory CreateFactory(
        string environmentName = "Development",
        CapturingEmailSender? emailSender = null,
        Dictionary<string, string?>? extraConfig = null,
        IStartupFilter? extraStartupFilter = null) =>
        new(connectionString, environmentName, emailSender, extraConfig, extraStartupFilter);

    private static DavetiyeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;

        return new DavetiyeDbContext(options);
    }

    private static async Task RunMigratorAsync(string connectionString)
    {
        var migratorAssembly = FindMigratorAssembly();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(migratorAssembly);

        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the database migrator process.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        Assert.True(
            process.ExitCode == 0,
            $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static string FindMigratorAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var path = Path.Combine(
            repositoryRoot, "tools", "Davetiye.DatabaseMigrator", "bin", configuration, "net10.0",
            "Davetiye.DatabaseMigrator.dll");

        Assert.True(File.Exists(path), $"Migrator assembly was not built: {path}");
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Kind, IReadOnlyDictionary<string, string> Data)> Sent { get; } = [];

        public Task SendAsync(
            string toEmail, string kind, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
        {
            Sent.Add((toEmail, kind, data));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stubs only the external-provider BOUNDARY: it establishes exactly the same
    /// <c>IdentityConstants.ExternalScheme</c> cookie a genuine Google OAuth round-trip would leave
    /// behind (using <c>SignInManager.ConfigureExternalAuthenticationProperties</c> so the property
    /// item Identity's own <c>GetExternalLoginInfoAsync</c> looks for is set exactly the way the real
    /// framework sets it, rather than a hand-guessed key). Everything downstream of this point
    /// (<c>/api/v1/auth/google/complete</c>, <see cref="GoogleSignInService"/>) is real, unmodified
    /// production code. Mirrors <c>ApiContractTests</c>' test-only-<c>IStartupFilter</c> convention.
    /// </summary>
    private sealed class GoogleLoginSimulationStartupFilter : IStartupFilter
    {
        public const string Path = "/__test-diagnostics/simulate-google-login";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);

            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == Path)
                {
                    var sub = context.Request.Query["sub"].ToString();
                    var email = context.Request.Query["email"].ToString();
                    var name = context.Request.Query["name"].ToString();
                    // Defaults to "true" (a normal, verified Google account) when the caller does not
                    // pass this explicitly, so every existing test that predates the email_verified
                    // check keeps exercising the success path unchanged; a test that needs to prove
                    // the fail-closed rejection passes "false" explicitly.
                    var emailVerified = context.Request.Query["emailVerified"].ToString();

                    var identity = new ClaimsIdentity("Test");
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                    if (!string.IsNullOrEmpty(email))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Email, email));
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Name, name));
                    }

                    identity.AddClaim(new Claim(
                        GoogleClaimTypes.EmailVerified, string.IsNullOrEmpty(emailVerified) ? "true" : emailVerified));

                    var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                    var properties = signInManager.ConfigureExternalAuthenticationProperties(
                        GoogleAuthenticationSchemeNames.Google, redirectUrl: null);

                    await context.SignInAsync(
                        IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);

                    context.Response.StatusCode = StatusCodes.Status200OK;
                    return;
                }

                await nextMiddleware(context);
            });
        };
    }

    /// <summary>
    /// Proves the "MfaComplete" authorization policy contract works end to end through the REAL
    /// middleware/endpoint pipeline, not just a direct <see cref="IAuthorizationService"/> call: this
    /// maps an actual minimal-API endpoint decorated with
    /// <c>.RequireAuthorization(AuthorizationPolicyNames.MfaComplete)</c>, so the request has to pass
    /// through ASP.NET Core's own routing/<c>UseAuthorization()</c> endpoint-metadata enforcement
    /// exactly the way a real <c>[Authorize(Policy = "MfaComplete")]</c>/
    /// <c>RequireAuthorization("MfaComplete")</c> production endpoint would — matching how
    /// "SuperAdminOnly" is already proven via the real <c>/admin/mfa/enroll</c>/<c>/verify</c>
    /// endpoints. <c>WebApplication</c> (the concrete type behind <see cref="IApplicationBuilder"/>
    /// in minimal hosting) also implements <see cref="IEndpointRouteBuilder"/>, so this route is
    /// added to the exact same endpoint data source the real production endpoints use — added
    /// exclusively from the test project via <see cref="IStartupFilter"/>, never part of the
    /// production Davetiye.Api assembly or route table, matching
    /// <c>ApiContractTests.ThrowingDiagnosticsStartupFilter</c>'s test-only-fixture convention.
    ///
    /// Uses <c>IApplicationBuilder.UseEndpoints</c> rather than casting <c>app</c> to
    /// <see cref="IEndpointRouteBuilder"/> directly: under <c>WebApplicationFactory</c> (which hosts
    /// Program.cs through the generic host's <c>IWebHostBuilder</c>/<c>GenericWebHostService</c>
    /// path, not a bare <c>WebApplication</c>), the concrete <c>app</c> instance passed into
    /// <see cref="IStartupFilter.Configure"/> is a plain <c>ApplicationBuilder</c>, which does not
    /// implement <see cref="IEndpointRouteBuilder"/>. <c>UseEndpoints</c> is the public,
    /// officially-supported way to add endpoints onto the SAME endpoint route table the implicit
    /// routing WebApplication already wired up for Program.cs's own real endpoints, from any
    /// <see cref="IApplicationBuilder"/> — it reads the route builder WebApplication's implicit
    /// <c>UseRouting</c> already stored, so this endpoint joins the exact same routing tree.
    /// </summary>
    private sealed class MfaCompletePolicyProbeStartupFilter : IStartupFilter
    {
        public const string Path = "/__test-diagnostics/mfa-complete";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);

            app.UseEndpoints(endpoints =>
                endpoints.MapGet(Path, () => Results.Ok())
                    .RequireAuthorization(AuthorizationPolicyNames.MfaComplete));
        };
    }

    private sealed class AdminTestWebApplicationFactory(
        string connectionString,
        string environmentName,
        CapturingEmailSender? emailSender,
        Dictionary<string, string?>? extraConfig,
        IStartupFilter? extraStartupFilter) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                var config = new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                    ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                    ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                };

                if (extraConfig is not null)
                {
                    foreach (var (key, value) in extraConfig)
                    {
                        config[key] = value;
                    }
                }

                configurationBuilder.AddInMemoryCollection(config);
            });

            if (emailSender is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(emailSender));
            }

            if (extraStartupFilter is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(extraStartupFilter));
            }
        }
    }
}
