using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// M6a's security-integration release gate (docs/THREAT_MODEL.md §12 gate 4/9): proves, over a real
/// HTTP host backed by a real PostgreSQL database, that register/confirm/login/logout/reset actually
/// work end to end, that a banned account is rejected, that antiforgery/CORS/rate-limiting are real
/// (not just configuration-shaped), that a password reset genuinely revokes a prior session, and that
/// the raw-HTTP fail-closed rule for Production holds while health checks stay reachable.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AuthEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string AllowedOrigin = "https://allowed.example.test";
    private const string ValidPassword = "TestPassw0rd1";
    private const string AnotherValidPassword = "NewPassw0rd2";
    private const string AuthCookieNamePrefix = "davetiye-auth-dev=";

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_confirm_email_login_then_logout_round_trips_a_real_authenticated_cookie()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var authCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);
        var (csrfCookie, csrfToken) = await GetCsrfAsync(client);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/logout", UriKind.Relative));
        logoutRequest.Headers.Add("Cookie", $"{authCookie}; {csrfCookie}");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        var logoutResponse = await client.SendAsync(logoutRequest);

        // Proves the cookie from Login genuinely authenticates: Logout requires authorization, and
        // succeeding here (rather than 401) shows the cookie was accepted as a valid principal.
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task Session_access_is_pii_free_and_classifies_anonymous_and_creator_sessions()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();

        var anonymousResponse = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Equal("no-store", anonymousResponse.Headers.CacheControl?.ToString());
        using (var anonymousBody = JsonDocument.Parse(await anonymousResponse.Content.ReadAsStringAsync()))
        {
            Assert.False(anonymousBody.RootElement.GetProperty("authenticated").GetBoolean());
            Assert.Equal("none", anonymousBody.RootElement.GetProperty("access").GetString());
            Assert.Equal(2, anonymousBody.RootElement.EnumerateObject().Count());
        }

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var cookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/session", UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        var creatorResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, creatorResponse.StatusCode);
        using var creatorBody = JsonDocument.Parse(await creatorResponse.Content.ReadAsStringAsync());
        Assert.True(creatorBody.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal("creator", creatorBody.RootElement.GetProperty("access").GetString());
        Assert.Equal(2, creatorBody.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Login_before_confirming_email_is_rejected_until_the_email_is_confirmed()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var registerResponse = await RegisterAsync(client, email, ValidPassword);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginBeforeConfirmResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = ValidPassword });

        // RequireConfirmedAccount=true makes Identity reject this login before it ever checks the
        // password, regardless of whether the password is right or wrong.
        Assert.Equal(HttpStatusCode.Forbidden, loginBeforeConfirmResponse.StatusCode);

        var confirmation = emailSender.Sent.Single(message =>
            message.Kind == EmailNotificationKinds.EmailConfirmation && message.ToEmail == email);
        var (userId, token) = ExtractUserIdAndToken(confirmation.Data["confirmationLink"]);
        var confirmResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var loginAfterConfirmResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, loginAfterConfirmResponse.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_banned_accounts_credentials_is_rejected()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await BanAccountAsync(email);

        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_without_an_antiforgery_token_is_rejected()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var authCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/logout", UriKind.Relative));
        logoutRequest.Headers.Add("Cookie", authCookie);
        var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task Cross_origin_form_urlencoded_post_to_login_cannot_authenticate()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        // Locks in the real reason /auth/login is safe without an antiforgery filter (see the
        // comment on AuthEndpoints.MapAuthEndpoints): a plain cross-site HTML <form> can only submit
        // as application/x-www-form-urlencoded (never application/json) and is not subject to CORS
        // at all (CORS only gates whether cross-origin JavaScript can read a response, not whether a
        // native <form> submission reaches the server). The only thing standing between an
        // attacker's cross-site <form> and a real login is that this endpoint only binds
        // LoginRequest from a JSON body - if that ever changed (e.g. adding [FromForm] support),
        // this test would start failing instead of silently reintroducing login-CSRF.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email,
                ["password"] = ValidPassword,
            }),
        };
        request.Headers.Add("Origin", "https://attacker.example.test");

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Cors_reflects_only_the_allowlisted_origin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/info", UriKind.Relative));
        allowedRequest.Headers.Add("Origin", AllowedOrigin);
        var allowedResponse = await client.SendAsync(allowedRequest);
        Assert.True(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedValues));
        Assert.Equal(AllowedOrigin, Assert.Single(allowedValues!));

        using var disallowedRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/info", UriKind.Relative));
        disallowedRequest.Headers.Add("Origin", "https://not-allowed.example.test");
        var disallowedResponse = await client.SendAsync(disallowedRequest);
        Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Register_endpoint_is_rate_limited_after_the_configured_permit_count()
    {
        await using var factory = CreateFactory(extraConfig: new Dictionary<string, string?>
        {
            ["AuthRateLimits:Register:PermitLimit"] = "2",
            ["AuthRateLimits:Register:WindowSeconds"] = "60",
        });
        using var client = factory.CreateClient();

        var first = await RegisterAsync(client, UniqueEmail(), ValidPassword);
        var second = await RegisterAsync(client, UniqueEmail(), ValidPassword);
        var third = await RegisterAsync(client, UniqueEmail(), ValidPassword);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Email_confirmation_password_reset_and_antiforgery_routes_are_rate_limited()
    {
        await using var factory = CreateFactory(extraConfig: new Dictionary<string, string?>
        {
            ["AuthRateLimits:EmailConfirmation:PermitLimit"] = "1",
            ["AuthRateLimits:EmailConfirmation:WindowSeconds"] = "60",
            ["AuthRateLimits:PasswordResetConfirm:PermitLimit"] = "1",
            ["AuthRateLimits:PasswordResetConfirm:WindowSeconds"] = "60",
            ["AuthRateLimits:AntiforgeryToken:PermitLimit"] = "1",
            ["AuthRateLimits:AntiforgeryToken:WindowSeconds"] = "60",
        });
        using var client = factory.CreateClient();

        var firstConfirmation = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = Guid.NewGuid(), token = "invalid" });
        var secondConfirmation = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = Guid.NewGuid(), token = "invalid" });

        var firstReset = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId = Guid.NewGuid(), token = "invalid", newPassword = ValidPassword });
        var secondReset = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId = Guid.NewGuid(), token = "invalid", newPassword = ValidPassword });

        var firstAntiforgery = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        var secondAntiforgery = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstConfirmation.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondConfirmation.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstReset.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondReset.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstAntiforgery.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondAntiforgery.StatusCode);
    }

    [Fact]
    public async Task Password_reset_request_returns_an_identical_response_for_existing_and_non_existing_email()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        var existingResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative),
            new { email });
        var nonExistingResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative),
            new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.OK, existingResponse.StatusCode);
        Assert.Equal(existingResponse.StatusCode, nonExistingResponse.StatusCode);
        var existingBody = await existingResponse.Content.ReadAsStringAsync();
        var nonExistingBody = await nonExistingResponse.Content.ReadAsStringAsync();
        Assert.Equal(existingBody, nonExistingBody);
    }

    [Fact]
    public async Task Password_reset_genuinely_revokes_a_prior_session()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        var oldAuthCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);

        emailSender.Sent.Clear();
        var resetRequestResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative),
            new { email });
        Assert.Equal(HttpStatusCode.OK, resetRequestResponse.StatusCode);

        var resetMessage = emailSender.Sent.Single(m => m.Kind == EmailNotificationKinds.PasswordReset);
        var (userId, token) = ExtractUserIdAndToken(resetMessage.Data["resetLink"]);

        var resetResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/reset-password", UriKind.Relative),
            new { userId, token, newPassword = AnotherValidPassword });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var (csrfCookie, csrfToken) = await GetCsrfAsync(client);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/logout", UriKind.Relative));
        logoutRequest.Headers.Add("Cookie", $"{oldAuthCookie}; {csrfCookie}");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        var logoutWithOldCookieResponse = await client.SendAsync(logoutRequest);

        // The OLD cookie (captured before the reset) must no longer authenticate anything: the
        // reset changed the security stamp, and SecurityStampValidator.ValidatePrincipalAsync
        // (wired in SecurityServiceCollectionExtensions) rejects the stale principal on this request.
        Assert.Equal(HttpStatusCode.Unauthorized, logoutWithOldCookieResponse.StatusCode);
    }

    [Fact]
    public async Task Production_without_a_configured_email_adapter_fails_closed_instead_of_silently_sending_no_email()
    {
        await using var factory = CreateFactory(environmentName: "Production");

        // Unlike the raw-HTTP test below, this request must actually reach the endpoint handler
        // (which needs IEmailSender), so it is dispatched as if it arrived over HTTPS.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var registerResponse = await RegisterAsync(client, UniqueEmail(), ValidPassword);

        // No real IEmailSender adapter exists yet in this phase (docs/ARCHITECTURE.md §5).
        // DependencyInjection.AddInfrastructure only wires DevEmailSender in Development, mirroring
        // docs/THREAT_MODEL.md §9's FakePaymentGateway production-startup-failure posture, so
        // Production fails closed here instead of silently dropping the confirmation email.
        Assert.Equal(HttpStatusCode.InternalServerError, registerResponse.StatusCode);

        var body = await registerResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IEmailSender", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raw_http_request_to_an_auth_endpoint_is_rejected_in_production_while_health_checks_still_work()
    {
        await using var factory = CreateFactory(environmentName: "Production");
        using var client = factory.CreateClient();

        // No X-Forwarded-Proto header is sent, so the request is indistinguishable from a raw,
        // unproxied HTTP request even though it is delivered in-process by TestServer.
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email = UniqueEmail(), password = ValidPassword });
        Assert.Equal(HttpStatusCode.BadRequest, loginResponse.StatusCode);

        var liveResponse = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        var readyResponse = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password, displayName = "Test User" });

    private static async Task RegisterAndConfirmAsync(
        HttpClient client, CapturingEmailSender emailSender, string email, string password)
    {
        var registerResponse = await RegisterAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var confirmation = emailSender.Sent.Single(message =>
            message.Kind == EmailNotificationKinds.EmailConfirmation && message.ToEmail == email);
        var (userId, token) = ExtractUserIdAndToken(confirmation.Data["confirmationLink"]);

        var confirmResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
    }

    private static async Task<string> LoginAndCaptureCookieAsync(HttpClient client, string email, string password)
    {
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies));

        return setCookies!
            .Select(ExtractCookiePair)
            .First(pair => pair.StartsWith(AuthCookieNamePrefix, StringComparison.Ordinal));
    }

    private static async Task<(string Cookie, string Token)> GetCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status {response.StatusCode}: {body}");
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));

        // The auth cookie may also be renewed (SlidingExpiration) on this same response if the
        // client's automatic cookie container is already carrying an authenticated session, so pick
        // out the antiforgery cookie specifically rather than assuming there is exactly one.
        var cookie = ExtractCookiePair(setCookies!.First(value =>
            !value.StartsWith(AuthCookieNamePrefix, StringComparison.Ordinal)));

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("token").GetString()!;

        return (cookie, token);
    }

    private static string ExtractCookiePair(string setCookieHeader) => setCookieHeader.Split(';', 2)[0];

    private static (string UserId, string Token) ExtractUserIdAndToken(string link)
    {
        var uri = new Uri(link);
        var query = QueryHelpers.ParseQuery(uri.Query);
        return (query["userId"].ToString(), query["token"].ToString());
    }

    private static string UniqueEmail() => $"auth-test-{Guid.NewGuid():N}@example.test";

    private async Task BanAccountAsync(string email)
    {
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);

        context.BanRecords.Add(BanRecord.Create(
            Guid.NewGuid(), account.Id, "Integration test ban", DateTimeOffset.UtcNow, Guid.NewGuid()));
        await context.SaveChangesAsync();
    }

    private AuthTestWebApplicationFactory CreateFactory(
        string environmentName = "Development",
        CapturingEmailSender? emailSender = null,
        Dictionary<string, string?>? extraConfig = null) =>
        new(connectionString, environmentName, emailSender, extraConfig);

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
            repositoryRoot,
            "tools",
            "Davetiye.DatabaseMigrator",
            "bin",
            configuration,
            "net10.0",
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
            string toEmail,
            string kind,
            IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken)
        {
            Sent.Add((toEmail, kind, data));
            return Task.CompletedTask;
        }
    }

    private sealed class AuthTestWebApplicationFactory(
        string connectionString,
        string environmentName,
        CapturingEmailSender? emailSender,
        Dictionary<string, string?>? extraConfig) : WebApplicationFactory<Program>
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
        }
    }
}
