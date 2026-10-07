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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}" +
            "&name=Test+User&returnUrl=%2Fcreator%2Fdashboard&marketingOptIn=true",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);
        AssertExternalCookieCleared(completeResponse);
        Assert.Equal(
            "https://davetiye.example.test/creator/dashboard",
            completeResponse.Headers.Location?.OriginalString);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.True(user.EmailConfirmed);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);
        Assert.Equal(AccountType.Individual, account.AccountType);
        var consentRecords = await context.AccountConsentRecords.Where(record => record.AccountId == account.Id).ToListAsync();
        Assert.Contains(consentRecords, record => record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement && record.Granted);
        Assert.Contains(consentRecords, record => record.Kind == AccountConsentKind.MarketingPreference && record.Granted);
    }

    [Fact]
    public async Task Google_new_account_creation_requires_acknowledgement_from_protected_external_state()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = UniqueEmail();

        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(email)}&serviceNoticeAcknowledged=false",
            UriKind.Relative));
        var response = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateDbContext(connectionString);
        Assert.False(await context.Users.AnyAsync(user => user.Email == email));
        Assert.Empty(await context.AccountConsentRecords.ToListAsync());
    }

    [Fact]
    public async Task Intermediate_authentication_cookies_are_short_non_sliding_and_google_complete_clears_external_cookie()
    {
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig());
        using (var scope = factory.Services.CreateScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CookieAuthenticationOptions>>();
            foreach (var scheme in new[] { IdentityConstants.ExternalScheme, IdentityConstants.TwoFactorUserIdScheme })
            {
                var cookie = options.Get(scheme);
                Assert.Equal(TimeSpan.FromMinutes(5), cookie.ExpireTimeSpan);
                Assert.False(cookie.SlidingExpiration);
            }
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertExternalCookieCleared(response);
    }

    [Fact]
    public async Task Google_first_time_sign_in_uses_the_protected_organization_account_type()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        var simulateResponse = await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(email)}" +
            "&accountType=Organization",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);
        Assert.Equal(AccountType.Organization, account.AccountType);
    }

    [Fact]
    public async Task Google_challenge_get_is_unavailable_even_when_query_contains_account_and_consent()
    {
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri(
            "/api/v1/auth/google/challenge?accountType=Individual&serviceNoticeAcknowledged=true&marketingOptIn=true",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Google_challenge_rejects_an_invalid_account_type_after_antiforgery_validation()
    {
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (csrfCookie, csrfToken) = await GetFormCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/challenge")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountType"] = "Administrator",
                ["serviceNoticeAcknowledged"] = "true",
                ["__RequestVerificationToken"] = csrfToken,
            }),
        };
        request.Headers.Add("Cookie", csrfCookie);
        request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Google_challenge_rejects_missing_antiforgery_token_and_cross_site_origin_without_side_effects()
    {
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/challenge")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountType"] = "Individual",
                ["returnUrl"] = "/panel",
                ["serviceNoticeAcknowledged"] = "true",
                ["marketingOptIn"] = "true",
            }),
        };
        request.Headers.Add("Origin", "https://evil.example.test");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var sameOriginWithoutToken = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/challenge")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountType"] = "Individual",
                ["returnUrl"] = "/panel",
                ["serviceNoticeAcknowledged"] = "true",
                ["marketingOptIn"] = "true",
            }),
        };
        sameOriginWithoutToken.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        var sameOriginResponse = await client.SendAsync(sameOriginWithoutToken);
        Assert.Equal(HttpStatusCode.BadRequest, sameOriginResponse.StatusCode);
        Assert.False(sameOriginResponse.Headers.Contains("Set-Cookie"));

        await using var context = CreateDbContext(connectionString);
        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.Accounts.ToListAsync());
        Assert.Empty(await context.AccountConsentRecords.ToListAsync());
    }

    [Fact]
    public async Task Google_challenge_accepts_a_valid_antiforgery_form_and_starts_oauth_redirect()
    {
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), registerGoogleHandlerForChallenge: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (csrfCookie, csrfToken) = await GetFormCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/challenge")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["accountType"] = "Organization",
                ["returnUrl"] = "/panel",
                ["serviceNoticeAcknowledged"] = "true",
                ["marketingOptIn"] = "false",
                ["__RequestVerificationToken"] = csrfToken,
            }),
        };
        request.Headers.Add("Cookie", csrfCookie);
        request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        var response = await client.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Status {response.StatusCode}: {responseBody}");
        Assert.Contains("accounts.google.com", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
        Assert.Contains(setCookies!, cookie => cookie.Contains(".AspNetCore.Correlation.", StringComparison.Ordinal));
        await using var context = CreateDbContext(connectionString);
        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.AccountConsentRecords.ToListAsync());
    }

    [Fact]
    public async Task Google_completion_rejects_an_invalid_account_type_even_if_external_state_is_malformed()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(email)}" +
            "&accountType=Administrator",
            UriKind.Relative));

        var response = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateDbContext(connectionString);
        Assert.False(await context.Users.AnyAsync(u => u.Email == email));
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

        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);
        Assert.Equal(
            "https://davetiye.example.test/giris/google-baglanti?returnUrl=%2F",
            completeResponse.Headers.Location?.OriginalString);
        AssertExternalCookieRetainedForConfirmation(completeResponse);

        // No silent merge: the account still has no Google login attached.
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == user.Id));
    }

    [Fact]
    public async Task Google_account_linking_end_to_end_links_after_password_login_confirmation()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        var sub = Guid.NewGuid().ToString();
        var simulateResponse = await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, simulateResponse.StatusCode);

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);
        Assert.Equal(
            "https://davetiye.example.test/giris/google-baglanti?returnUrl=%2F",
            completeResponse.Headers.Location?.OriginalString);

        // Proves ownership of the existing account by logging in with its password - the External-
        // scheme cookie from the simulated Google attempt above is still present in the same client's
        // cookie jar alongside the fresh Application-scheme cookie this establishes.
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrf = await GetCsrfTokenAsync(client);
        var confirmResponse = await PostWithCsrfAsync(
            client, "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        AssertExternalCookieCleared(confirmResponse);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.True(await context.UserLogins.AnyAsync(login => login.UserId == user.Id && login.ProviderKey == sub));

        // A fresh, unrelated client now signs in through the already-linked fast path using the same
        // Google identity, proving the link is real (not just a 200 that changed nothing).
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var secondSimulateResponse = await secondClient.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, secondSimulateResponse.StatusCode);
        var secondCompleteResponse = await secondClient.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, secondCompleteResponse.StatusCode);
    }

    [Fact]
    public async Task Google_account_link_confirm_rejects_a_wrong_current_password_without_linking()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });

        var sub = Guid.NewGuid().ToString();
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));

        var csrf = await GetCsrfTokenAsync(client);
        var response = await PostWithCsrfAsync(
            client, "/api/v1/auth/google/link/confirm", new { password = "WrongPassw0rd1" }, csrf);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertExternalCookieCleared(response);
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == user.Id));
    }

    [Fact]
    public async Task Google_account_link_confirm_without_a_pending_external_login_returns_bad_request()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });

        var csrf = await GetCsrfTokenAsync(client);
        var response = await PostWithCsrfAsync(
            client, "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Google_account_link_confirm_without_antiforgery_is_rejected_without_linking()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });

        var sub = Guid.NewGuid().ToString();
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/google/link/confirm", UriKind.Relative),
            new { password = ValidPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == user.Id));
    }

    [Fact]
    public async Task Concurrent_google_account_link_confirmations_do_not_create_duplicates_or_return_server_errors()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });

        var sub = Guid.NewGuid().ToString();
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(email)}",
            UriKind.Relative));
        var csrf = await GetCsrfTokenAsync(client);

        using var firstRequest = CreatePostWithCsrfRequest(
            "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);
        using var secondRequest = CreatePostWithCsrfRequest(
            "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);

        var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        Assert.All(
            responses,
            response => Assert.Contains(
                response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.Equal(
            1,
            await context.UserLogins.CountAsync(login =>
                login.UserId == user.Id && login.ProviderKey == sub));
    }

    [Fact]
    public async Task Google_account_link_redirect_uses_only_the_protected_sanitized_local_return_path()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(email)}" +
            "&returnUrl=%2Fpanel%3Ftab%3Ddrafts",
            UriKind.Relative));

        var response = await client.GetAsync(new Uri(
            "/api/v1/auth/google/complete?returnUrl=https%3A%2F%2Fevil.example%2Fsteal",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "https://davetiye.example.test/giris/google-baglanti?returnUrl=%2Fpanel%3Ftab%3Ddrafts",
            response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(email, response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Google_account_link_confirm_rejects_an_email_mismatch_between_pending_login_and_authenticated_account()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // A pending Google identity for a DIFFERENT email than the authenticated account. This scopes
        // linking to exactly the same-email collision the feature exists to resolve.
        var otherEmail = UniqueEmail();
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(otherEmail)}",
            UriKind.Relative));

        var csrf = await GetCsrfTokenAsync(client);
        var confirmResponse = await PostWithCsrfAsync(
            client, "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);

        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == user.Id));
    }

    [Fact]
    public async Task Google_account_link_confirm_rejects_a_google_identity_already_linked_to_another_account()
    {
        var emailSender = new CapturingEmailSender();
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(
            extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation, emailSender: emailSender);

        // Account A signs up via Google directly (first-time sign-in), linking Google sub S to A.
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var emailA = UniqueEmail();
        var sub = Guid.NewGuid().ToString();
        await firstClient.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(emailA)}&name=Account+A",
            UriKind.Relative));
        var firstCompleteResponse = await firstClient.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, firstCompleteResponse.StatusCode);

        // Account B is a separate password account that attempts to link the SAME Google identity
        // (sub S) - the simulated external cookie uses B's own email so the email-match check
        // passes, but AddLoginAsync itself must still refuse it because sub S is already associated
        // with A.
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var emailB = UniqueEmail();
        await RegisterAndConfirmAsync(secondClient, emailSender, emailB, ValidPassword);
        var loginBResponse = await secondClient.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email = emailB, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, loginBResponse.StatusCode);

        await secondClient.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(emailB)}",
            UriKind.Relative));

        var csrf = await GetCsrfTokenAsync(secondClient);
        var confirmResponse = await PostWithCsrfAsync(
            secondClient, "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);

        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        var userB = await context.Users.SingleAsync(u => u.Email == emailB);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == userB.Id));
    }

    [Fact]
    public async Task Google_account_link_confirm_refuses_to_link_a_super_admin_account()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var adminEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, adminEmail, ValidPassword);

        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email = adminEmail, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(adminEmail)}",
            UriKind.Relative));

        var csrf = await GetCsrfTokenAsync(client);
        var confirmResponse = await PostWithCsrfAsync(
            client, "/api/v1/auth/google/link/confirm", new { password = ValidPassword }, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, confirmResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        var admin = await context.Users.SingleAsync(u => u.Email == adminEmail);
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == admin.Id));
    }

    [Fact]
    public async Task Google_sign_in_never_offers_linking_for_a_super_admin_email_collision()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var adminEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, adminEmail, ValidPassword);

        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={Guid.NewGuid()}&email={Uri.EscapeDataString(adminEmail)}",
            UriKind.Relative));

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, completeResponse.StatusCode);
    }

    /// <summary>
    /// This is the M1-required, code-enforced proof for the invariant
    /// <c>GoogleSignInService.CompleteSignInAsync</c>'s doc comment describes: before this
    /// milestone's linking feature existed, "a Google-linked identity can never belong to a Super
    /// Admin" was a code-UNENFORCED assumption, safe only because no code path could ever attach a
    /// Google login to a Super Admin identity. This test proves the fast path itself now refuses such
    /// an identity independently of how it came to exist - it directly attaches a Google login to a
    /// bootstrapped Super Admin (bypassing <c>ConfirmLinkAsync</c> entirely, which would itself
    /// refuse this - see <see cref="Google_account_link_confirm_refuses_to_link_a_super_admin_account"/>)
    /// to simulate a state that must never be reachable through any real endpoint, then proves
    /// <c>CompleteSignInAsync</c> still fails closed rather than bypassing two-factor and signing in.
    /// </summary>
    [Fact]
    public async Task Google_sign_in_fast_path_refuses_a_super_admin_identity_even_if_a_google_login_is_already_attached()
    {
        var loginSimulation = new GoogleLoginSimulationStartupFilter();
        await using var factory = CreateFactory(extraConfig: GoogleEnabledConfig(), extraStartupFilter: loginSimulation);

        var adminEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, adminEmail, ValidPassword);

        var sub = Guid.NewGuid().ToString();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync(adminEmail);
            Assert.NotNull(admin);
            var addLoginResult = await userManager.AddLoginAsync(
                admin!, new UserLoginInfo(GoogleAuthenticationSchemeNames.Google, sub, "Google"));
            Assert.True(addLoginResult.Succeeded);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync(new Uri(
            $"{GoogleLoginSimulationStartupFilter.Path}?sub={sub}&email={Uri.EscapeDataString(adminEmail)}",
            UriKind.Relative));

        var completeResponse = await client.GetAsync(new Uri("/api/v1/auth/google/complete", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, completeResponse.StatusCode);

        // Proves no session was ever established - not just that the response happened to be
        // Forbidden. This is exactly the check that would fail if bypassTwoFactor: true were still
        // called before the Super Admin check (the pre-fix ordering).
        var sessionResponse = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        using var sessionBody = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.False(sessionBody.RootElement.GetProperty("authenticated").GetBoolean());
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
        using (var firstFactorSession = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative)))
        using (var firstFactorJson = JsonDocument.Parse(await firstFactorSession.Content.ReadAsStringAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, firstFactorSession.StatusCode);
            Assert.Equal("mfa-setup-required-super-admin", firstFactorJson.RootElement.GetProperty("access").GetString());
        }

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
        using (var completedSession = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative)))
        using (var completedSessionJson = JsonDocument.Parse(await completedSession.Content.ReadAsStringAsync()))
        {
            Assert.Equal("mfa-complete-super-admin", completedSessionJson.RootElement.GetProperty("access").GetString());
        }

        // MFA-complete Super Admins cannot replace an active factor by calling the setup endpoint.
        var guardedEnrollCsrf = await GetCsrfTokenAsync(client);
        using var guardedEnroll = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, guardedEnrollCsrf);
        Assert.Equal(HttpStatusCode.Conflict, guardedEnroll.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var enabledUser = await userManager.FindByEmailAsync(email);
            Assert.NotNull(enabledUser);
            Assert.Equal(sharedKey, await userManager.GetAuthenticatorKeyAsync(enabledUser!));
        }

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
    public async Task Lost_factor_recovery_requires_acknowledgments_clears_factors_audits_and_revokes_sessions_immediately()
    {
        await using var factory = CreateFactory(extraConfig: new Dictionary<string, string?>
        {
            ["AuthCookie:SecurityStampValidationIntervalSeconds"] = "900"
        });
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, email, ValidPassword);
        var operatorEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, operatorEmail, ValidPassword);

        await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        var enrollCsrf = await GetCsrfTokenAsync(client);
        using var enroll = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollCsrf);
        using var enrollBody = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var oldKey = enrollBody.RootElement.GetProperty("sharedKey").GetString()!;
        var verifyCsrf = await GetCsrfTokenAsync(client);
        using var verify = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/verify",
            new { code = GenerateTotpCode(oldKey) }, verifyCsrf);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        using var verifyBody = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var oldRecoveryCode = verifyBody.RootElement.GetProperty("recoveryCodes")[0].GetString()!;
        await LogoutAsync(client);

        await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        using var twoFactor = await client.PostAsJsonAsync(new Uri("/api/v1/admin/mfa/login/complete", UriKind.Relative),
            new { code = GenerateTotpCode(oldKey), isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.OK, twoFactor.StatusCode);
        using var activeSession = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        using var activeBody = JsonDocument.Parse(await activeSession.Content.ReadAsStringAsync());
        Assert.Equal("mfa-complete-super-admin", activeBody.RootElement.GetProperty("access").GetString());

        Guid userId;
        Guid operatorId;
        string? oldStamp;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            userId = user!.Id;
            operatorId = (await userManager.FindByEmailAsync(operatorEmail))!.Id;
            oldStamp = await userManager.GetSecurityStampAsync(user);

            var runner = new AdminMfaLostFactorRecoveryRunner(
                userManager,
                scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>(),
                scope.ServiceProvider.GetRequiredService<Davetiye.Application.Modules.Administration.Contracts.IAdminAuditWriter>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
                userId, Guid.NewGuid(), confirmed: false, outOfBandIdentityVerified: true, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
                userId, Guid.NewGuid(), confirmed: true, outOfBandIdentityVerified: false, CancellationToken.None));
            Assert.Equal(oldStamp, await userManager.GetSecurityStampAsync(user));
            Assert.True(await userManager.GetTwoFactorEnabledAsync(user));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var runner = new AdminMfaLostFactorRecoveryRunner(
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>(),
                scope.ServiceProvider.GetRequiredService<Davetiye.Application.Modules.Administration.Contracts.IAdminAuditWriter>());
            Assert.Equal(AdminMfaLostFactorRecoveryOutcome.Recovered, await runner.RunAsync(
                userId, operatorId, confirmed: true, outOfBandIdentityVerified: true, CancellationToken.None));
        }

        // Even with a 15-minute configured interval, privileged cookies validate their stamp on each request.
        using var revokedSession = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, revokedSession.StatusCode);
        using (var revokedBody = JsonDocument.Parse(await revokedSession.Content.ReadAsStringAsync()))
            Assert.False(revokedBody.RootElement.GetProperty("authenticated").GetBoolean());

        await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        using var setupSession = await client.GetAsync(new Uri("/api/v1/auth/session", UriKind.Relative));
        using var setupBody = JsonDocument.Parse(await setupSession.Content.ReadAsStringAsync());
        Assert.Equal("mfa-setup-required-super-admin", setupBody.RootElement.GetProperty("access").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            Assert.NotNull(user);
            Assert.False(await userManager.GetTwoFactorEnabledAsync(user!));
            Assert.NotEqual(oldStamp, await userManager.GetSecurityStampAsync(user!));
            Assert.False((await userManager.GetAuthenticatorKeyAsync(user!)) == oldKey);
            Assert.False((await userManager.RedeemTwoFactorRecoveryCodeAsync(user!, oldRecoveryCode)).Succeeded);

            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var audit = await db.AdminAuditRecords.SingleAsync(record => record.SubjectId == userId);
            Assert.Equal(operatorId, audit.ActorId);
            Assert.Equal("SuperAdminMfaLostFactorRecovered", audit.EventType);
        }
    }

    [Fact]
    public async Task Lost_factor_recovery_refuses_non_admins_and_super_admin_identities_with_creator_accounts()
    {
        await using var factory = CreateFactory();
        var adminEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, adminEmail, ValidPassword);
        var operatorEmail = UniqueEmail();
        await BootstrapSuperAdminAsync(factory, operatorEmail, ValidPassword);
        var creatorEmail = UniqueEmail();
        Guid adminId;
        Guid creatorId;
        Guid operatorId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var admin = await userManager.FindByEmailAsync(adminEmail);
            Assert.NotNull(admin);
            adminId = admin!.Id;
            operatorId = (await userManager.FindByEmailAsync(operatorEmail))!.Id;
            var creator = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = creatorEmail, Email = creatorEmail, EmailConfirmed = true
            };
            Assert.True((await userManager.CreateAsync(creator, ValidPassword)).Succeeded);
            creatorId = creator.Id;
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(
                Guid.NewGuid(), adminId, AccountType.Individual, "Invalid mixed identity", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var runner = new AdminMfaLostFactorRecoveryRunner(
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>(),
                scope.ServiceProvider.GetRequiredService<Davetiye.Application.Modules.Administration.Contracts.IAdminAuditWriter>());
            Assert.Equal(AdminMfaLostFactorRecoveryOutcome.RefusedCreatorAccount, await runner.RunAsync(
                adminId, operatorId, confirmed: true, outOfBandIdentityVerified: true, CancellationToken.None));
            Assert.Equal(AdminMfaLostFactorRecoveryOutcome.RefusedNotSuperAdmin, await runner.RunAsync(
                creatorId, operatorId, confirmed: true, outOfBandIdentityVerified: true, CancellationToken.None));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Empty(await db.AdminAuditRecords.ToListAsync());
        }
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

    private static async Task<(string Cookie, string Token)> GetFormCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Status {response.StatusCode}: {body}");
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));

        var cookie = setCookies!
            .Select(value => value.Split(';', 2)[0])
            .First(value => !value.StartsWith("davetiye-auth-dev=", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(body);
        return (cookie, document.RootElement.GetProperty("token").GetString()!);
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client, string path, object body, string csrfToken)
    {
        using var request = CreatePostWithCsrfRequest(path, body, csrfToken);
        return await client.SendAsync(request);
    }

    private static void AssertExternalCookieCleared(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
        Assert.Contains(setCookieHeaders!, header =>
            header.Contains(IdentityConstants.ExternalScheme, StringComparison.Ordinal) &&
            (header.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertExternalCookieRetainedForConfirmation(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            return;
        Assert.DoesNotContain(setCookieHeaders!, header =>
            header.Contains(IdentityConstants.ExternalScheme, StringComparison.Ordinal) &&
            (header.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
             header.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));
    }

    private static HttpRequestMessage CreatePostWithCsrfRequest(string path, object body, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return request;
    }

    private static async Task RegisterAndConfirmAsync(
        HttpClient client, CapturingEmailSender emailSender, string email, string password)
    {
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password, displayName = "Test User", accountType = "Individual", serviceNoticeAcknowledged = true });
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
        Assert.Empty(uri.Query);
        var query = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
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
        IStartupFilter? extraStartupFilter = null,
        bool registerGoogleHandlerForChallenge = false) =>
        new(connectionString, environmentName, emailSender, extraConfig, extraStartupFilter, registerGoogleHandlerForChallenge);

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
                    var accountType = context.Request.Query["accountType"].ToString();
                    var returnUrl = context.Request.Query["returnUrl"].ToString();
                    var serviceNoticeAcknowledged = context.Request.Query["serviceNoticeAcknowledged"].ToString();
                    var marketingOptIn = context.Request.Query["marketingOptIn"].ToString();

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
                    properties.Items["davetiye:account_type"] =
                        string.IsNullOrWhiteSpace(accountType) ? "Individual" : accountType;
                    properties.Items["davetiye:return_path"] =
                        string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
                    properties.Items["davetiye:service_notice_acknowledged"] =
                        string.IsNullOrWhiteSpace(serviceNoticeAcknowledged) ? "true" : serviceNoticeAcknowledged;
                    properties.Items["davetiye:marketing_opt_in"] =
                        string.IsNullOrWhiteSpace(marketingOptIn) ? "false" : marketingOptIn;

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
        IStartupFilter? extraStartupFilter,
        bool registerGoogleHandlerForChallenge) : WebApplicationFactory<Program>
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

            if (registerGoogleHandlerForChallenge)
            {
                builder.ConfigureTestServices(services => services.AddAuthentication().AddGoogle("Google", options =>
                {
                    options.ClientId = "test-client-id";
                    options.ClientSecret = "test-client-secret";
                    options.CallbackPath = "/api/v1/auth/google/oauth-callback";
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                }));
            }

            if (extraStartupFilter is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(extraStartupFilter));
            }
        }
    }
}
