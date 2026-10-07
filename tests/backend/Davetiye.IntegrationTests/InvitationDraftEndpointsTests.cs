using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// M3's real HTTP/PostgreSQL release gate. These tests intentionally create two authenticated
/// Creator accounts and exercise the API using their server-managed cookies, proving that an
/// internal invitation id is never sufficient to read or mutate another Creator's draft.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class InvitationDraftEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string ValidPassword = "TestPassw0rd1";
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Individual_checkout_uses_database_price_and_enforces_account_purchase_idempotency()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = new InvitationApiFactory(connectionString, emailSender);
        using var creator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var otherCreator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var organizationCreator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var anonymous = factory.CreateClient();
        await RegisterConfirmAndLoginAsync(creator, emailSender, UniqueEmail());
        await RegisterConfirmAndLoginAsync(otherCreator, emailSender, UniqueEmail());
        await RegisterConfirmAndLoginAsync(organizationCreator, emailSender, UniqueEmail(), "Organization");
        var csrf = await GetCsrfTokenAsync(creator);
        var otherCsrf = await GetCsrfTokenAsync(otherCreator);
        var organizationCsrf = await GetCsrfTokenAsync(organizationCreator);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(new Uri("/api/v1/payments/plans", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await organizationCreator.GetAsync(new Uri("/api/v1/payments/plans", UriKind.Relative))).StatusCode);

        var created = await SendJsonAsync(creator, HttpMethod.Post, "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } }, csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var invitationId = createdBody.RootElement.GetProperty("id").GetGuid();
        var organizationDraft = await SendJsonAsync(organizationCreator, HttpMethod.Post, "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } }, organizationCsrf);
        Assert.Equal(HttpStatusCode.Created, organizationDraft.StatusCode);
        using var organizationDraftBody = await ReadJsonAsync(organizationDraft);
        var organizationInvitationId = organizationDraftBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendCheckoutAsync(organizationCreator, organizationInvitationId, "standard", "organization-checkout-01", organizationCsrf)).StatusCode);

        var catalog = await creator.GetAsync(new Uri("/api/v1/payments/plans", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Contains("no-store", catalog.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        using (var catalogBody = await ReadJsonAsync(catalog))
        {
            var items = catalogBody.RootElement.EnumerateArray().ToArray();
            Assert.Equal(new[] { "standard", "premium" }, items.Select(item => item.GetProperty("key").GetString()).ToArray());
            Assert.Equal(699m, items[0].GetProperty("amount").GetDecimal());
            Assert.Equal(1199m, items[1].GetProperty("amount").GetDecimal());
            Assert.All(items, item =>
            {
                Assert.Equal("TRY", item.GetProperty("currency").GetString());
                Assert.Equal("one-time", item.GetProperty("billingPeriod").GetString());
            });
        }

        var invalidOrganizationPlan = await SendCheckoutAsync(creator, invitationId, "organization", "checkout-key-00000001", csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrganizationPlan.StatusCode);
        using (var tamperedRequest = new HttpRequestMessage(HttpMethod.Post,
                   new Uri($"/api/v1/invitations/{invitationId}/checkout", UriKind.Relative))
               {
                   Content = JsonContent.Create(new { planKey = "standard", amount = 1m, currency = "USD" })
               })
        {
            tamperedRequest.Headers.Add("X-CSRF-TOKEN", csrf);
            tamperedRequest.Headers.Add("Idempotency-Key", "checkout-key-tampered-01");
            Assert.Equal(HttpStatusCode.BadRequest, (await creator.SendAsync(tamperedRequest)).StatusCode);
        }
        var foreignInvitation = await SendCheckoutAsync(otherCreator, invitationId, "standard", "checkout-key-foreign-01", otherCsrf);
        Assert.Equal(HttpStatusCode.NotFound, foreignInvitation.StatusCode);
        using (var missingIdempotency = new HttpRequestMessage(HttpMethod.Post,
                   new Uri($"/api/v1/invitations/{invitationId}/checkout", UriKind.Relative))
               {
                   Content = JsonContent.Create(new { planKey = "standard" })
               })
        {
            missingIdempotency.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.BadRequest, (await creator.SendAsync(missingIdempotency)).StatusCode);
        }
        var missingCsrf = await creator.PostAsJsonAsync(new Uri($"/api/v1/invitations/{invitationId}/checkout", UriKind.Relative),
            new { planKey = "standard" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        const string key = "checkout-key-concurrent-01";
        var concurrentResponses = await Task.WhenAll(
            SendCheckoutAsync(creator, invitationId, "standard", key, csrf),
            SendCheckoutAsync(creator, invitationId, "standard", key, csrf));
        Assert.All(concurrentResponses, response => Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Unexpected concurrent checkout response {(int)response.StatusCode}: {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}"));
        Assert.All(concurrentResponses.Where(response => response.StatusCode == HttpStatusCode.Created),
            response => Assert.Null(response.Headers.Location));
        var parsed = new List<JsonDocument>();
        foreach (var response in concurrentResponses) parsed.Add(await ReadJsonAsync(response));
        var attemptId = parsed[0].RootElement.GetProperty("attemptId").GetGuid();
        Assert.All(parsed, body => Assert.Equal(attemptId, body.RootElement.GetProperty("attemptId").GetGuid()));
        Assert.All(parsed, body =>
        {
            Assert.Equal(699m, body.RootElement.GetProperty("amount").GetDecimal());
            Assert.Equal("TRY", body.RootElement.GetProperty("currency").GetString());
            Assert.Equal("Pending", body.RootElement.GetProperty("status").GetString());
            if (body.RootElement.GetProperty("checkoutUrl").ValueKind == JsonValueKind.String)
                Assert.Contains("fake-checkout", body.RootElement.GetProperty("checkoutUrl").GetString());
        });
        foreach (var body in parsed) body.Dispose();
        foreach (var response in concurrentResponses) response.Dispose();

        var distinctRaceDraft = await SendJsonAsync(creator, HttpMethod.Post, "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } }, csrf);
        Assert.Equal(HttpStatusCode.Created, distinctRaceDraft.StatusCode);
        using var distinctRaceDraftBody = await ReadJsonAsync(distinctRaceDraft);
        var distinctRaceInvitationId = distinctRaceDraftBody.RootElement.GetProperty("id").GetGuid();
        var distinctKeyRace = await Task.WhenAll(
            SendCheckoutAsync(creator, distinctRaceInvitationId, "standard", "checkout-key-race-a-000001", csrf),
            SendCheckoutAsync(creator, distinctRaceInvitationId, "premium", "checkout-key-race-b-000001", csrf));
        Assert.Single(distinctKeyRace, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(distinctKeyRace, response => response.StatusCode == HttpStatusCode.Conflict);
        await using (var db = CreateDbContext(connectionString))
            Assert.Equal(1, await db.PaymentAttempts.CountAsync(item => item.InvitationId == distinctRaceInvitationId));
        foreach (var response in distinctKeyRace) response.Dispose();

        var changedPlan = await SendCheckoutAsync(creator, invitationId, "premium", key, csrf);
        Assert.Equal(HttpStatusCode.Conflict, changedPlan.StatusCode);
        var parallelPurchase = await SendCheckoutAsync(creator, invitationId, "premium", "checkout-key-new-000001", csrf);
        Assert.Equal(HttpStatusCode.Conflict, parallelPurchase.StatusCode);

        await using (var db = CreateDbContext(connectionString))
        {
            Assert.Equal(1, await db.PaymentAttempts.CountAsync(item => item.InvitationId == invitationId));
            Assert.Equal(0, await db.AccountPlanGrants.CountAsync(item => item.AssignedInvitationId == invitationId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE payment_attempts SET status = 'Failed' WHERE id = {attemptId}");
            await db.Database.ExecuteSqlRawAsync("UPDATE plans SET is_active = FALSE WHERE key = 'standard'");
        }

        var failedReplay = await SendCheckoutAsync(creator, invitationId, "standard", key, csrf);
        Assert.Equal(HttpStatusCode.OK, failedReplay.StatusCode);
        using (var failedReplayBody = await ReadJsonAsync(failedReplay))
        {
            Assert.Equal(attemptId, failedReplayBody.RootElement.GetProperty("attemptId").GetGuid());
            Assert.Equal("Failed", failedReplayBody.RootElement.GetProperty("status").GetString());
        }

        var retry = await SendCheckoutAsync(creator, invitationId, "premium", "checkout-key-retry-000001", csrf);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        using var retryBody = await ReadJsonAsync(retry);
        Assert.Equal("premium", retryBody.RootElement.GetProperty("planKey").GetString());
        Assert.Equal(1199m, retryBody.RootElement.GetProperty("amount").GetDecimal());
        Assert.NotEqual(attemptId, retryBody.RootElement.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task Checkout_provider_cancellation_persists_unknown_and_blocks_new_attempts()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = new InvitationApiFactory(connectionString, emailSender,
            paymentGateway: new CancelingPaymentGateway());
        using var creator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterConfirmAndLoginAsync(creator, emailSender, UniqueEmail());
        var csrf = await GetCsrfTokenAsync(creator);
        var created = await SendJsonAsync(creator, HttpMethod.Post, "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } }, csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var invitationId = createdBody.RootElement.GetProperty("id").GetGuid();

        const string key = "checkout-cancel-key-000001";
        var timedOut = await SendCheckoutAsync(creator, invitationId, "standard", key, csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, timedOut.StatusCode);
        await using (var db = CreateDbContext(connectionString))
        {
            var attempt = await db.PaymentAttempts.SingleAsync(item => item.InvitationId == invitationId);
            Assert.Equal("Unknown", attempt.Status);
        }

        var sameAttempt = await SendCheckoutAsync(creator, invitationId, "standard", key, csrf);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, sameAttempt.StatusCode);
        var newAttempt = await SendCheckoutAsync(creator, invitationId, "premium", "checkout-new-key-000001", csrf);
        Assert.Equal(HttpStatusCode.Conflict, newAttempt.StatusCode);
        await using (var db = CreateDbContext(connectionString))
            Assert.Equal(1, await db.PaymentAttempts.CountAsync(item => item.InvitationId == invitationId));
    }

    [Fact]
    public async Task Draft_crud_autosave_concurrency_and_cross_creator_BOLA_are_enforced_over_real_HTTP()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = new InvitationApiFactory(connectionString, emailSender);
        using var creatorA = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var creatorB = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        await RegisterConfirmAndLoginAsync(creatorA, emailSender, UniqueEmail());
        await RegisterConfirmAndLoginAsync(creatorB, emailSender, UniqueEmail());

        var missingCsrf = await creatorA.PostAsJsonAsync(
            new Uri("/api/v1/invitations", UriKind.Relative),
            new { contentSchemaVersion = 1, content = new { } });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var creatorACsrf = await GetCsrfTokenAsync(creatorA);
        var createResponse = await SendJsonAsync(
            creatorA,
            HttpMethod.Post,
            "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } },
            creatorACsrf);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = await ReadJsonAsync(createResponse);
        var invitationId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(0, created.RootElement.GetProperty("invitationRevision").GetInt64());
        Assert.Equal(0, created.RootElement.GetProperty("contentRevision").GetInt64());

        var noTemplateValidation = await creatorA.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, noTemplateValidation.StatusCode);
        Assert.True(noTemplateValidation.Headers.CacheControl?.NoStore);
        using (var validation = await ReadJsonAsync(noTemplateValidation))
        {
            Assert.False(validation.RootElement.GetProperty("templateSelected").GetBoolean());
            Assert.False(validation.RootElement.GetProperty("templateAvailable").GetBoolean());
            Assert.Empty(validation.RootElement.GetProperty("requiredFields").EnumerateArray());
            Assert.Empty(validation.RootElement.GetProperty("recommendedFields").EnumerateArray());
        }

        var nullContent = await SendJsonAsync(
            creatorA,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new { contentSchemaVersion = 1, content = (object?)null, expectedContentRevision = 0 },
            creatorACsrf);
        Assert.Equal(HttpStatusCode.BadRequest, nullContent.StatusCode);

        var listA = await creatorA.GetAsync(new Uri("/api/v1/invitations", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, listA.StatusCode);
        using (var listBody = await ReadJsonAsync(listA))
        {
            Assert.Equal(1, listBody.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(invitationId, listBody.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        }

        var zeroPageSize = await creatorA.GetAsync(
            new Uri("/api/v1/invitations?page=1&pageSize=0", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, zeroPageSize.StatusCode);

        var listB = await creatorB.GetAsync(new Uri("/api/v1/invitations", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, listB.StatusCode);
        using (var listBody = await ReadJsonAsync(listB))
        {
            Assert.Equal(0, listBody.RootElement.GetProperty("totalCount").GetInt32());
        }

        var foreignRead = await creatorB.GetAsync(new Uri($"/api/v1/invitations/{invitationId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);

        var foreignValidation = await creatorB.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreignValidation.StatusCode);
        Assert.True(foreignValidation.Headers.CacheControl?.NoStore);

        var creatorBCsrf = await GetCsrfTokenAsync(creatorB);
        var foreignWrite = await SendJsonAsync(
            creatorB,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new
            {
                contentSchemaVersion = 1,
                content = new { headline = "Foreign write" },
                expectedContentRevision = 0,
            },
            creatorBCsrf);
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);

        // The template lookup is deliberately after the owner lookup: a foreign Creator gets the
        // same 404 even with a nonsense key, avoiding ownership/catalog information leakage.
        var foreignTemplateWrite = await SendJsonAsync(
            creatorB,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/template",
            new { templateKey = "does-not-exist", expectedInvitationRevision = 0 },
            creatorBCsrf);
        Assert.Equal(HttpStatusCode.NotFound, foreignTemplateWrite.StatusCode);

        var autosave = await SendJsonAsync(
            creatorA,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new
            {
                contentSchemaVersion = 1,
                content = new { eventType = "dugun", headline = "Ada ve Grace" },
                expectedContentRevision = 0,
            },
            creatorACsrf);
        Assert.Equal(HttpStatusCode.OK, autosave.StatusCode);
        using (var autosaved = await ReadJsonAsync(autosave))
        {
            Assert.Equal(1, autosaved.RootElement.GetProperty("contentRevision").GetInt64());
            Assert.Equal("Ada ve Grace", autosaved.RootElement.GetProperty("content").GetProperty("headline").GetString());
        }

        var staleAutosave = await SendJsonAsync(
            creatorA,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new
            {
                contentSchemaVersion = 1,
                content = new { headline = "Stale overwrite" },
                expectedContentRevision = 0,
            },
            creatorACsrf);
        Assert.Equal(HttpStatusCode.Conflict, staleAutosave.StatusCode);
        using (var conflict = await ReadJsonAsync(staleAutosave))
        {
            Assert.Equal(1, conflict.RootElement.GetProperty("currentContentRevision").GetInt64());
        }

        var finalRead = await creatorA.GetAsync(new Uri($"/api/v1/invitations/{invitationId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, finalRead.StatusCode);
        using var finalDraft = await ReadJsonAsync(finalRead);
        Assert.Equal("Ada ve Grace", finalDraft.RootElement.GetProperty("content").GetProperty("headline").GetString());
    }

    [Fact]
    public async Task Validation_report_uses_active_template_metadata_and_safe_presence_semantics()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = new InvitationApiFactory(connectionString, emailSender);
        using var creator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        await RegisterConfirmAndLoginAsync(creator, emailSender, UniqueEmail());
        var csrf = await GetCsrfTokenAsync(creator);
        var create = await SendJsonAsync(
            creator,
            HttpMethod.Post,
            "/api/v1/invitations",
            new
            {
                contentSchemaVersion = 1,
                templateKey = "gece-kina",
                content = new
                {
                    headline = "Kina Gecesi",
                    startsAt = "2027-06-12T17:00:00+03:00",
                    venue = new { name = "   " },
                    hostNames = Array.Empty<string>(),
                    programItems = new[] { new { title = (string?)null } },
                },
            },
            csrf);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadJsonAsync(create);
        var invitationId = created.RootElement.GetProperty("id").GetGuid();

        var incompleteResponse = await creator.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, incompleteResponse.StatusCode);
        using (var incomplete = await ReadJsonAsync(incompleteResponse))
        {
            Assert.True(incomplete.RootElement.GetProperty("templateSelected").GetBoolean());
            Assert.True(incomplete.RootElement.GetProperty("templateAvailable").GetBoolean());
            Assert.Equal(1, incomplete.RootElement.GetProperty("invitationRevision").GetInt64());
            Assert.Equal(0, incomplete.RootElement.GetProperty("contentRevision").GetInt64());
            AssertFieldPresence(incomplete.RootElement, "requiredFields", "headline", expected: true);
            AssertFieldPresence(incomplete.RootElement, "requiredFields", "startsAt", expected: true);
            AssertFieldPresence(incomplete.RootElement, "requiredFields", "venue.name", expected: false);
            AssertFieldPresence(incomplete.RootElement, "recommendedFields", "message", expected: false);
            AssertFieldPresence(incomplete.RootElement, "recommendedFields", "hostNames", expected: false);
            AssertFieldPresence(incomplete.RootElement, "recommendedFields", "programItems", expected: false);
        }

        var complete = await SendJsonAsync(
            creator,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new
            {
                contentSchemaVersion = 1,
                expectedContentRevision = 0,
                content = new
                {
                    headline = "Kina Gecesi",
                    startsAt = "2027-06-12T17:00:00+03:00",
                    venue = new { name = "Bogaz Salonu" },
                    message = "Sizi aramizda gormek isteriz.",
                    hostNames = new[] { "Aileler" },
                    programItems = new[] { new { startsAt = "2027-06-12T18:00:00+03:00" } },
                },
            },
            csrf);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var completeResponse = await creator.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        using var completeReport = await ReadJsonAsync(completeResponse);
        Assert.All(
            completeReport.RootElement.GetProperty("requiredFields").EnumerateArray(),
            item => Assert.True(item.GetProperty("isPresent").GetBoolean()));
        Assert.All(
            completeReport.RootElement.GetProperty("recommendedFields").EnumerateArray(),
            item => Assert.True(item.GetProperty("isPresent").GetBoolean()));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var template = await dbContext.TemplateDefinitions.SingleAsync(item => item.Key == "gece-kina");
            template.UpdateMetadata(
                template.Name,
                template.Category,
                template.IsPremium,
                template.PreviewImageUrl,
                template.SupportedModules,
                """["headline","unknownFutureField"]""",
                template.RecommendedFields);
            await dbContext.SaveChangesAsync();
        }

        var unknownFieldResponse = await creator.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        using (var unknownFieldReport = await ReadJsonAsync(unknownFieldResponse))
        {
            var unknown = unknownFieldReport.RootElement.GetProperty("requiredFields")
                .EnumerateArray()
                .Single(item => item.GetProperty("field").GetString() == "unknownFutureField");
            Assert.False(unknown.GetProperty("isRecognized").GetBoolean());
            Assert.False(unknown.GetProperty("isPresent").GetBoolean());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var template = await dbContext.TemplateDefinitions.SingleAsync(item => item.Key == "gece-kina");
            template.SetActive(false);
            await dbContext.SaveChangesAsync();
        }

        var unavailableResponse = await creator.GetAsync(
            new Uri($"/api/v1/invitations/{invitationId}/validation", UriKind.Relative));
        using var unavailable = await ReadJsonAsync(unavailableResponse);
        Assert.True(unavailable.RootElement.GetProperty("templateSelected").GetBoolean());
        Assert.True(unavailable.RootElement.GetProperty("templateAvailable").GetBoolean());
        AssertFieldPresence(unavailable.RootElement, "requiredFields", "headline", expected: true);
        var unavailableUnknownField = unavailable.RootElement.GetProperty("requiredFields")
            .EnumerateArray()
            .Single(item => item.GetProperty("field").GetString() == "unknownFutureField");
        Assert.False(unavailableUnknownField.GetProperty("isRecognized").GetBoolean());
        Assert.False(unavailableUnknownField.GetProperty("isPresent").GetBoolean());
        Assert.All(
            unavailable.RootElement.GetProperty("recommendedFields").EnumerateArray(),
            item => Assert.True(item.GetProperty("isPresent").GetBoolean()));
    }

    [Fact]
    public async Task Draft_command_logs_are_structured_and_do_not_include_content_payloads()
    {
        var emailSender = new CapturingEmailSender();
        var logProvider = new CapturingLoggerProvider();
        await using var factory = new InvitationApiFactory(connectionString, emailSender, logProvider: logProvider);
        using var creator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        await RegisterConfirmAndLoginAsync(creator, emailSender, UniqueEmail());
        var csrf = await GetCsrfTokenAsync(creator);
        var sensitiveContent = $"never-log-{Guid.NewGuid():N}";
        var create = await SendJsonAsync(
            creator,
            HttpMethod.Post,
            "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { headline = sensitiveContent } },
            csrf);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadJsonAsync(create);
        var invitationId = created.RootElement.GetProperty("id").GetGuid();

        var autosave = await SendJsonAsync(
            creator,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/draft",
            new
            {
                contentSchemaVersion = 1,
                content = new { headline = sensitiveContent, message = sensitiveContent },
                expectedContentRevision = 0,
            },
            csrf);
        Assert.Equal(HttpStatusCode.OK, autosave.StatusCode);

        var selectTemplate = await SendJsonAsync(
            creator,
            HttpMethod.Put,
            $"/api/v1/invitations/{invitationId}/template",
            new { templateKey = "zamansiz-dugun", expectedInvitationRevision = 0 },
            csrf);
        Assert.Equal(HttpStatusCode.OK, selectTemplate.StatusCode);

        Assert.Contains(logProvider.Entries, entry => entry.Contains("Invitation draft created", StringComparison.Ordinal));
        Assert.Contains(logProvider.Entries, entry => entry.Contains("Invitation draft autosaved", StringComparison.Ordinal));
        Assert.Contains(logProvider.Entries, entry => entry.Contains("Invitation draft template selected", StringComparison.Ordinal));
        Assert.DoesNotContain(logProvider.Entries, entry => entry.Contains(sensitiveContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invitation_endpoints_require_an_authenticated_cookie()
    {
        await using var factory = new InvitationApiFactory(connectionString, new CapturingEmailSender());
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(new Uri("/api/v1/invitations", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_auth_capabilities_are_always_mapped_pii_free_and_not_cached()
    {
        await using var factory = new InvitationApiFactory(connectionString, new CapturingEmailSender());
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(new Uri("/api/v1/auth/capabilities", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = await ReadJsonAsync(response);
        Assert.False(body.RootElement.GetProperty("googleSignInEnabled").GetBoolean());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Public_auth_capabilities_reflect_an_enabled_google_provider_without_exposing_its_configuration()
    {
        await using var factory = new InvitationApiFactory(
            connectionString,
            new CapturingEmailSender(),
            new Dictionary<string, string?>
            {
                ["GoogleAuth:Enabled"] = "true",
                ["GoogleAuth:ClientId"] = "test-client-id",
                ["GoogleAuth:ClientSecret"] = "test-client-secret",
                ["GoogleAuth:CallbackBaseUrl"] = "http://localhost:5000/api/v1/auth/google/oauth-callback",
            });
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(new Uri("/api/v1/auth/capabilities", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.GetProperty("googleSignInEnabled").GetBoolean());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Creator_RSVP_configuration_seeds_defaults_enforces_revision_and_scopes_questions_to_owner()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = new InvitationApiFactory(connectionString, emailSender);
        using var creator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var foreignCreator = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterConfirmAndLoginAsync(creator, emailSender, UniqueEmail());
        await RegisterConfirmAndLoginAsync(foreignCreator, emailSender, UniqueEmail());
        var csrf = await GetCsrfTokenAsync(creator);

        var create = await SendJsonAsync(creator, HttpMethod.Post, "/api/v1/invitations",
            new { contentSchemaVersion = 1, content = new { } }, csrf);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadJsonAsync(create);
        var invitationId = created.RootElement.GetProperty("id").GetGuid();
        var path = $"/api/v1/invitations/{invitationId}/rsvp";

        var missingCsrf = await creator.PutAsJsonAsync(new Uri(path, UriKind.Relative), new { expectedRevision = 0, isEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var enable = await SendJsonAsync(creator, HttpMethod.Put, path,
            new { expectedRevision = 0, isEnabled = true }, csrf);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        using var enabled = await ReadJsonAsync(enable);
        Assert.True(enabled.RootElement.GetProperty("enabled").GetBoolean());
        var revision = enabled.RootElement.GetProperty("revision").GetInt64();
        var seeded = enabled.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(4, seeded.Length);
        Assert.Equal("ShortText", seeded[0].GetProperty("type").GetString());
        Assert.True(seeded[0].GetProperty("isRequired").GetBoolean());
        Assert.Equal("ParticipantCount", seeded[2].GetProperty("semanticRole").GetString());
        Assert.False(seeded[3].GetProperty("isRequired").GetBoolean());

        var deleteMiddle = await SendJsonAsync(creator, HttpMethod.Delete,
            $"{path}/questions/{seeded[1].GetProperty("id").GetGuid()}",
            new { expectedRevision = revision }, csrf);
        Assert.Equal(HttpStatusCode.OK, deleteMiddle.StatusCode);
        using var afterDelete = await ReadJsonAsync(deleteMiddle);
        revision = afterDelete.RootElement.GetProperty("revision").GetInt64();

        var addChoice = await SendJsonAsync(creator, HttpMethod.Post, $"{path}/questions",
            new
            {
                expectedRevision = revision,
                prompt = "Meal",
                type = "SingleChoice",
                isRequired = true,
                semanticRole = (string?)null,
                options = new[] { new { id = (Guid?)null, label = "Vegetarian", sortOrder = 0 }, new { id = (Guid?)null, label = "Meat", sortOrder = 1 } },
            }, csrf);
        Assert.True(addChoice.StatusCode == HttpStatusCode.OK,
            $"Expected RSVP question add to succeed but got {(int)addChoice.StatusCode}: {await addChoice.Content.ReadAsStringAsync()}");
        using var withChoice = await ReadJsonAsync(addChoice);
        var choice = withChoice.RootElement.GetProperty("questions").EnumerateArray()
            .Single(item => item.GetProperty("prompt").GetString() == "Meal");
        Assert.Equal(4, choice.GetProperty("sortOrder").GetInt32());
        var choiceId = choice.GetProperty("id").GetGuid();
        var oldOptions = choice.GetProperty("options").EnumerateArray().ToArray();
        var retainedOptionId = oldOptions[0].GetProperty("id").GetGuid();
        var omittedOptionId = oldOptions[1].GetProperty("id").GetGuid();
        revision = withChoice.RootElement.GetProperty("revision").GetInt64();

        var updateChoice = await SendJsonAsync(creator, HttpMethod.Put, $"{path}/questions/{choiceId}",
            new
            {
                expectedRevision = revision,
                prompt = "Meal choice",
                type = "SingleChoice",
                isRequired = true,
                semanticRole = (string?)null,
                options = new[] { new { id = (Guid?)retainedOptionId, label = "Plant based", sortOrder = 0 }, new { id = (Guid?)null, label = "Vegan", sortOrder = 1 } },
            }, csrf);
        Assert.Equal(HttpStatusCode.OK, updateChoice.StatusCode);
        using var updated = await ReadJsonAsync(updateChoice);
        var updatedChoice = updated.RootElement.GetProperty("questions").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == choiceId);
        Assert.Equal(retainedOptionId, updatedChoice.GetProperty("options")[0].GetProperty("id").GetGuid());
        Assert.Equal("Plant based", updatedChoice.GetProperty("options")[0].GetProperty("label").GetString());
        Assert.DoesNotContain(updatedChoice.GetProperty("options").EnumerateArray(), item => item.GetProperty("id").GetGuid() == omittedOptionId);

        var stale = await SendJsonAsync(creator, HttpMethod.Put, path,
            new { expectedRevision = revision, isEnabled = false }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var foreignGet = await foreignCreator.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        var foreignCsrf = await GetCsrfTokenAsync(foreignCreator);
        var foreignWrite = await SendJsonAsync(foreignCreator, HttpMethod.Put, path,
            new { expectedRevision = 0, isEnabled = false }, foreignCsrf);
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
    }

    private static async Task RegisterConfirmAndLoginAsync(HttpClient client, CapturingEmailSender emailSender, string email,
        string accountType = "Individual")
    {
        var registration = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = ValidPassword, displayName = "Test Creator", accountType, serviceNoticeAcknowledged = true });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var confirmation = emailSender.Sent.Single(message =>
            message.Kind == EmailNotificationKinds.EmailConfirmation && message.ToEmail == email);
        var (userId, token) = ExtractUserIdAndToken(confirmation.Data["confirmationLink"]);
        var confirmationResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative), new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);

        var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object payload,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendCheckoutAsync(HttpClient client, Guid invitationId,
        string planKey, string idempotencyKey, string csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"/api/v1/invitations/{invitationId}/checkout", UriKind.Relative))
        {
            Content = JsonContent.Create(new { planKey })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static DavetiyeDbContext CreateDbContext(string testConnectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(testConnectionString)
            .Options;
        return new DavetiyeDbContext(options);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static void AssertFieldPresence(
        JsonElement report,
        string collectionName,
        string field,
        bool expected)
    {
        var result = report.GetProperty(collectionName)
            .EnumerateArray()
            .Single(item => item.GetProperty("field").GetString() == field);
        Assert.True(result.GetProperty("isRecognized").GetBoolean());
        Assert.Equal(expected, result.GetProperty("isPresent").GetBoolean());
    }

    private static (string UserId, string Token) ExtractUserIdAndToken(string link)
    {
        var uri = new Uri(link);
        Assert.Empty(uri.Query);
        var query = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
        return (query["userId"].ToString(), query["token"].ToString());
    }

    private static string UniqueEmail() => $"invitation-test-{Guid.NewGuid():N}@example.test";

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var migratorAssembly = Path.Combine(FindRepositoryRoot(), "tools", "Davetiye.DatabaseMigrator", "bin", "Debug", "net10.0", "Davetiye.DatabaseMigrator.dll");
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
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{await standardOutputTask}{Environment.NewLine}{await standardErrorTask}");
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
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

    private sealed class InvitationApiFactory(
        string databaseConnectionString,
        CapturingEmailSender emailSender,
        IReadOnlyDictionary<string, string?>? extraConfiguration = null,
        ILoggerProvider? logProvider = null,
        IPaymentGateway? paymentGateway = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                var configuration = new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = databaseConnectionString,
                    ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                    ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                };
                if (extraConfiguration is not null)
                {
                    foreach (var (key, value) in extraConfiguration)
                    {
                        configuration[key] = value;
                    }
                }

                configurationBuilder.AddInMemoryCollection(configuration);
            });
            if (logProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(emailSender);
                if (paymentGateway is not null)
                {
                    services.RemoveAll<IPaymentGateway>();
                    services.AddSingleton(paymentGateway);
                }
            });
        }
    }

    private sealed class CancelingPaymentGateway : IPaymentGateway
    {
        public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request,
            CancellationToken cancellationToken) => throw new OperationCanceledException("Provider request timed out.");

        public bool IsTrustedCheckoutUrl(string checkoutUrl) => false;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(formatter(state, exception));
        }
    }
}
