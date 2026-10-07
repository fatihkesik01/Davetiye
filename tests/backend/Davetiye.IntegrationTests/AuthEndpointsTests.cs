using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    public async Task Verified_deletion_revokes_a_stale_cookie_and_keeps_public_guest_route_anonymous()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var staleCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);
        var (csrfCookie, csrfToken) = await GetCsrfAsync(client);

        using var startRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/deletion-requests");
        startRequest.Headers.Add("Cookie", $"{staleCookie}; {csrfCookie}");
        startRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        using var started = await client.SendAsync(startRequest);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.True(started.Headers.CacheControl?.NoStore);

        var deleteMail = Assert.Single(emailSender.Sent,
            message => message.Kind == EmailNotificationKinds.AccountDeletionConfirmation);
        var token = QueryHelpers.ParseQuery(new Uri(deleteMail.Data["confirmationLink"]).Fragment.TrimStart('#'))["token"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Empty(new Uri(deleteMail.Data["confirmationLink"]).Query);

        using var missingCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/deletion-requests/confirm")
        {
            Content = JsonContent.Create(new { token })
        };
        missingCsrf.Headers.Add("Cookie", csrfCookie);
        using var rejected = await client.SendAsync(missingCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using (var context = CreateDbContext(connectionString))
        {
            var account = await context.Accounts.Join(context.Users, item => item.IdentityUserId,
                user => user.Id, (item, user) => new { Account = item, user.Email }).SingleAsync(item => item.Email == email);
            Assert.Null(account.Account.DeletionStartedAtUtc);
        }

        using var confirm = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/deletion-requests/confirm")
        {
            Content = JsonContent.Create(new { token })
        };
        confirm.Headers.Add("Cookie", csrfCookie);
        confirm.Headers.Add("X-CSRF-TOKEN", csrfToken);
        using var confirmed = await client.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.True(confirmed.Headers.CacheControl?.NoStore);

        using var creatorRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/invitations");
        creatorRequest.Headers.Add("Cookie", staleCookie);
        using var creatorResponse = await client.SendAsync(creatorRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, creatorResponse.StatusCode);

        using var publicGuestRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/public/invitations/{new string('a', PublicInvitationCode.EncodedLength)}");
        publicGuestRequest.Headers.Add("Cookie", staleCookie);
        using var publicGuestResponse = await client.SendAsync(publicGuestRequest);
        Assert.Equal(HttpStatusCode.NotFound, publicGuestResponse.StatusCode);
    }

    [Fact]
    public async Task Register_with_organization_account_type_persists_an_organization_account()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = ValidPassword, displayName = "Test Org", accountType = "Organization", serviceNoticeAcknowledged = true });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        var account = await context.Accounts.SingleAsync(a => a.IdentityUserId == user.Id);
        Assert.Equal(AccountType.Organization, account.AccountType);
    }

    [Fact]
    public async Task Register_requires_service_notice_acknowledgement_and_records_default_off_marketing_preference()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var rejected = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = ValidPassword,
            displayName = "Test User",
            accountType = "Individual",
            marketingOptIn = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await using (var context = CreateDbContext(connectionString))
        {
            Assert.False(await context.Users.AnyAsync(user => user.Email == email));
            Assert.Empty(await context.AccountConsentRecords.ToListAsync());
        }

        var accepted = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = ValidPassword,
            displayName = "Test User",
            accountType = "Individual",
            serviceNoticeAcknowledged = true,
        });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        await using var acceptedContext = CreateDbContext(connectionString);
        var account = await acceptedContext.Accounts.Join(acceptedContext.Users,
            account => account.IdentityUserId, user => user.Id, (account, user) => new { account.Id, user.Email })
            .SingleAsync(item => item.Email == email);
        var records = await acceptedContext.AccountConsentRecords.Where(record => record.AccountId == account.Id)
            .OrderBy(record => record.Kind).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement && record.Granted);
        Assert.Contains(records, record => record.Kind == AccountConsentKind.MarketingPreference && !record.Granted);
    }

    [Fact]
    public async Task Creator_can_read_and_update_marketing_preference_with_antiforgery_and_audited_history()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);
        var authCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account/consents");
        getRequest.Headers.Add("Cookie", authCookie);
        using var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("no-store", getResponse.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("serviceNotice").GetProperty("acknowledged").GetBoolean());
        Assert.False(body.RootElement.GetProperty("marketing").GetProperty("optedIn").GetBoolean());
        Assert.Equal(2, body.RootElement.GetProperty("history").GetArrayLength());

        using var unprotectedRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/account/consents/marketing")
        {
            Content = JsonContent.Create(new { optedIn = true }),
        };
        unprotectedRequest.Headers.Add("Cookie", authCookie);
        using var unprotectedResponse = await client.SendAsync(unprotectedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, unprotectedResponse.StatusCode);

        var (csrfCookie, csrfToken) = await GetCsrfAsync(client);
        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/account/consents/marketing")
        {
            Content = JsonContent.Create(new { optedIn = true }),
        };
        updateRequest.Headers.Add("Cookie", $"{authCookie}; {csrfCookie}");
        updateRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        using var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.True(updatedBody.RootElement.GetProperty("marketing").GetProperty("optedIn").GetBoolean());
        Assert.Equal(3, updatedBody.RootElement.GetProperty("history").GetArrayLength());

        await using var context = CreateDbContext(connectionString);
        Assert.Equal(3, await context.AccountConsentRecords.CountAsync());
    }

    [Fact]
    public async Task Legacy_creator_session_is_gated_until_explicit_notice_acknowledgement()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(client, emailSender, email, ValidPassword);

        await using (var context = CreateDbContext(connectionString))
        {
            var account = await context.Accounts.Join(context.Users,
                account => account.IdentityUserId, user => user.Id, (account, user) => new { account.Id, user.Email })
                .SingleAsync(item => item.Email == email);
            var serviceNotice = await context.AccountConsentRecords.SingleAsync(record =>
                record.AccountId == account.Id && record.Kind == AccountConsentKind.ServiceNoticeAcknowledgement);
            context.AccountConsentRecords.Remove(serviceNotice);
            await context.SaveChangesAsync();
        }

        var authCookie = await LoginAndCaptureCookieAsync(client, email, ValidPassword);
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        sessionRequest.Headers.Add("Cookie", authCookie);
        using var sessionResponse = await client.SendAsync(sessionRequest);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        using (var sessionBody = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync()))
            Assert.True(sessionBody.RootElement.GetProperty("serviceNoticeRequired").GetBoolean());

        using var creatorRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/invitations");
        creatorRequest.Headers.Add("Cookie", authCookie);
        using var gatedResponse = await client.SendAsync(creatorRequest);
        Assert.Equal((HttpStatusCode)428, gatedResponse.StatusCode);
        Assert.Contains("no-store", gatedResponse.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        using var publicGuestRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/invitations/not-a-real-code");
        publicGuestRequest.Headers.Add("Cookie", authCookie);
        using var publicGuestResponse = await client.SendAsync(publicGuestRequest);
        Assert.Equal(HttpStatusCode.NotFound, publicGuestResponse.StatusCode);

        using var consentRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/account/consents");
        consentRequest.Headers.Add("Cookie", authCookie);
        using var consentResponse = await client.SendAsync(consentRequest);
        Assert.Equal(HttpStatusCode.OK, consentResponse.StatusCode);
        using (var consentBody = JsonDocument.Parse(await consentResponse.Content.ReadAsStringAsync()))
            Assert.False(consentBody.RootElement.GetProperty("serviceNotice").GetProperty("acknowledged").GetBoolean());

        var (csrfCookie, csrfToken) = await GetCsrfAsync(client);
        using var acknowledgeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/consents/service-notice")
        {
            Content = JsonContent.Create(new { acknowledged = true }),
        };
        acknowledgeRequest.Headers.Add("Cookie", $"{authCookie}; {csrfCookie}");
        acknowledgeRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        using var acknowledgeResponse = await client.SendAsync(acknowledgeRequest);
        Assert.Equal(HttpStatusCode.OK, acknowledgeResponse.StatusCode);
        using var acknowledgement = JsonDocument.Parse(await acknowledgeResponse.Content.ReadAsStringAsync());
        Assert.True(acknowledgement.RootElement.GetProperty("serviceNotice").GetProperty("acknowledged").GetBoolean());

        using var releasedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/invitations");
        releasedRequest.Headers.Add("Cookie", authCookie);
        using var releasedResponse = await client.SendAsync(releasedRequest);
        Assert.Equal(HttpStatusCode.OK, releasedResponse.StatusCode);
    }

    [Fact]
    public async Task Register_with_an_invalid_account_type_is_rejected_and_creates_nothing()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = ValidPassword, displayName = "Test User", accountType = "SuperOrg" });

        Assert.Equal(HttpStatusCode.BadRequest, registerResponse.StatusCode);

        await using var context = CreateDbContext(connectionString);
        Assert.False(await context.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Register_rolls_back_identity_user_and_account_when_confirmation_enqueue_fails()
    {
        var emailSender = new CapturingEmailSender { FailEnqueue = true };
        await using var factory = CreateFactory(emailSender: emailSender);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var response = await RegisterAsync(client, email, ValidPassword);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var context = CreateDbContext(connectionString);
        Assert.False(await context.Users.AnyAsync(user => user.Email == email));
        Assert.False(await context.Accounts.Join(context.Users, account => account.IdentityUserId, user => user.Id,
            (account, user) => user).AnyAsync(user => user.Email == email));
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
            Assert.False(anonymousBody.RootElement.GetProperty("serviceNoticeRequired").GetBoolean());
            Assert.Equal(3, anonymousBody.RootElement.EnumerateObject().Count());
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
        Assert.False(creatorBody.RootElement.GetProperty("serviceNoticeRequired").GetBoolean());
        Assert.Equal(3, creatorBody.RootElement.EnumerateObject().Count());

        Guid creatorIdentityId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            creatorIdentityId = await db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
            var sessionAccess = scope.ServiceProvider.GetRequiredService<IAuthSessionAccessService>();
            var mixed = await sessionAccess.GetAccessAsync(creatorIdentityId,
                hasSuperAdminClaim: true, hasMfaClaim: true, CancellationToken.None);
            Assert.Equal(SessionAccess.None, mixed.Access);
        }
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
        Assert.Equal(HttpStatusCode.Unauthorized, loginBeforeConfirmResponse.StatusCode);
        using var unknownAccountResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email = UniqueEmail(), password = ValidPassword });
        Assert.Equal(loginBeforeConfirmResponse.StatusCode, unknownAccountResponse.StatusCode);
        using var loginBeforeConfirmBody = JsonDocument.Parse(await loginBeforeConfirmResponse.Content.ReadAsStringAsync());
        using var unknownAccountBody = JsonDocument.Parse(await unknownAccountResponse.Content.ReadAsStringAsync());
        Assert.Equal(loginBeforeConfirmBody.RootElement.GetProperty("title").GetString(),
            unknownAccountBody.RootElement.GetProperty("title").GetString());

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
    public async Task Login_uses_same_public_status_and_title_for_unknown_wrong_unconfirmed_and_locked_accounts()
    {
        var sender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: sender, extraConfig: new Dictionary<string, string?>
        {
            ["AuthRateLimits:Login:PermitLimit"] = "30",
        });
        using var client = factory.CreateClient();

        var lockedEmail = UniqueEmail();
        await RegisterAndConfirmAsync(client, sender, lockedEmail, ValidPassword);
        using var wrongPassword = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email = lockedEmail, password = "WrongPassw0rd!" });
        using var unknownAccount = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email = UniqueEmail(), password = ValidPassword });

        var unconfirmedEmail = UniqueEmail();
        using var registration = await RegisterAsync(client, unconfirmedEmail, ValidPassword);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        using var unconfirmedAccount = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email = unconfirmedEmail, password = ValidPassword });

        var failedAttempts = new List<HttpResponseMessage>();
        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                failedAttempts.Add(await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
                    new { email = lockedEmail, password = "WrongPassw0rd!" }));
            }

            await using var db = CreateDbContext(connectionString);
            var user = await db.Users.SingleAsync(candidate => candidate.Email == lockedEmail);
            Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow, "The integration setup must reach Identity's lockout state.");

            using var lockedAccount = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
                new { email = lockedEmail, password = ValidPassword });
            var responses = new[] { wrongPassword, unknownAccount, unconfirmedAccount, lockedAccount };
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal("Invalid email or password.", await ReadProblemTitleAsync(response));
            }
            Assert.All(failedAttempts, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        }
        finally
        {
            foreach (var response in failedAttempts)
                response.Dispose();
        }
    }

    private static async Task<string?> ReadProblemTitleAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("title").GetString();
    }

    [Fact]
    public async Task Password_reset_destination_limit_is_independent_of_account_existence_and_keeps_generic_success()
    {
        var sender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender: sender, extraConfig: new Dictionary<string, string?>
        {
            ["AuthRateLimits:PasswordResetRequestDestination:PermitLimit"] = "1",
            ["AuthRateLimits:PasswordResetRequestDestination:WindowSeconds"] = "3600",
        });
        using var client = factory.CreateClient();
        var registeredEmail = UniqueEmail();
        await RegisterAndConfirmAsync(client, sender, registeredEmail, ValidPassword);

        using var knownResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative), new { email = registeredEmail });
        using var knownSecondResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative), new { email = registeredEmail });
        using var unknownResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/request-password-reset", UriKind.Relative), new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.OK, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, knownSecondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);
        Assert.Equal(await knownResponse.Content.ReadAsStringAsync(), await unknownResponse.Content.ReadAsStringAsync());
        Assert.Equal("no-store", knownSecondResponse.Headers.CacheControl?.ToString());
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
    public async Task Production_durably_queues_protected_email_without_provider_credentials()
    {
        await using var factory = CreateFactory(environmentName: "Production");

        // Unlike the raw-HTTP test below, this request must actually reach the endpoint handler
        // (which needs IEmailSender), so it is dispatched as if it arrived over HTTPS.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var email = UniqueEmail();
        var registerResponse = await RegisterAsync(client, email, ValidPassword);

        // Queue acceptance is durable even while the provider is not configured; the worker keeps
        // the message retryable and never stores the auth link in plaintext.
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var body = await registerResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IEmailSender", body, StringComparison.Ordinal);
        await using var context = CreateDbContext(connectionString);
        var queued = await context.OutboxMessages.SingleAsync(message => message.MessageType == "notifications.email");
        Assert.DoesNotContain(email, queued.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmationLink", queued.Payload, StringComparison.Ordinal);
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
            new { email, password, displayName = "Test User", accountType = "Individual", serviceNoticeAcknowledged = true });

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
        Assert.Empty(uri.Query);
        var query = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
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
        public bool FailEnqueue { get; init; }

        public Task SendAsync(
            string toEmail,
            string kind,
            IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken)
        {
            if (FailEnqueue)
                throw new InvalidOperationException("Simulated durable enqueue failure.");
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
                var databaseConnectionString = connectionString;
                var config = new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = databaseConnectionString,
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
