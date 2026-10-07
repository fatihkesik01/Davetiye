using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Analytics;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PublicInvitationExperienceTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "TestPassw0rd1";
    private const string Content = """{"headline":"Published headline","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Hall","address":"PRIVATE FULL ADDRESS","mapUrl":"https://evil.test/embed"},"message":"Welcome","hostNames":["Ada"]}""";
    private const string Modules = """, "contacts":[{"name":"Contact","role":"Organizer","phone":"+905551234567"}],"announcement":"Current announcement","faqs":[{"question":"When?","answer":"Today"}],"transportStops":[{"name":"Station","address":"Stop address","departureTime":"13:00"}]}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schema_one_remains_compatible_and_optional_modules_use_typed_Published_allowlist(bool modules)
    {
        await using var h = await CreateAsync(modules ? Content[..^1] + Modules : Content, renderer: modules ? 2 : 1);
        await h.Publish();
        await using (var db = h.Db())
        {
            var unknownFields = "{\"signedUrl\":\"secret-signed-url\",\"internalId\":\"secret\"}";
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE published_contents SET content = content || CAST({unknownFields} AS jsonb)");
            (await db.WorkingContents.SingleAsync()).ReplaceContent(Content.Replace("Published headline", "Private unsaved module changes"), 1, Now);
            await db.SaveChangesAsync();
        }
        using var client = h.Client();
        var response = await client.GetAsync(h.JsonPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoCache(response);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(1, json.RootElement.GetProperty("contentSchemaVersion").GetInt32());
        Assert.Equal(modules ? 2 : 1, json.RootElement.GetProperty("rendererVersion").GetInt32());
        var content = json.RootElement.GetProperty("content");
        Assert.Equal("Published headline", content.GetProperty("headline").GetString());
        Assert.DoesNotContain("Private unsaved", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mapUrl", text, StringComparison.Ordinal);
        Assert.DoesNotContain("signedUrl", text, StringComparison.Ordinal);
        Assert.DoesNotContain("internalId", text, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Invitation.ToString(), text, StringComparison.Ordinal);
        if (modules)
        {
            Assert.Equal("+905551234567", content.GetProperty("contacts")[0].GetProperty("phone").GetString());
            Assert.Equal("Current announcement", content.GetProperty("announcement").GetString());
            Assert.Equal("When?", content.GetProperty("faqs")[0].GetProperty("question").GetString());
            Assert.Equal("13:00", content.GetProperty("transportStops")[0].GetProperty("departureTime").GetString());
        }
    }

    [Fact]
    public async Task Public_media_changes_only_after_explicit_initial_publish_or_update()
    {
        await using var h = await CreateAsync();
        var firstId = Guid.NewGuid();
        var nextId = Guid.NewGuid();
        await using (var db = h.Db())
        {
            var first = ReadyImage(h.Invitation, firstId);
            var next = ReadyImage(h.Invitation, nextId);
            db.MediaAssets.AddRange(first, next);
            db.MediaPlacements.Add(first.Place(Guid.NewGuid(), MediaPresentationRole.Gallery, 0, Now));
            await db.SaveChangesAsync();
        }

        await h.Publish();
        using var client = h.Client();
        var beforeMutation = await client.GetFromJsonAsync<JsonElement>(h.JsonPath);
        Assert.Equal(firstId.ToString(), beforeMutation.GetProperty("media")[0].GetProperty("assetId").GetString());

        await using (var db = h.Db())
        {
            var firstPlacement = await db.MediaPlacements.SingleAsync(item => item.MediaAssetId == firstId);
            db.MediaPlacements.Remove(firstPlacement);
            db.MediaPlacements.Add((await db.MediaAssets.SingleAsync(item => item.Id == nextId))
                .Place(Guid.NewGuid(), MediaPresentationRole.Gallery, 0, Now.AddMinutes(1)));
            await db.SaveChangesAsync();
        }

        var beforeUpdate = await client.GetFromJsonAsync<JsonElement>(h.JsonPath);
        Assert.Equal(firstId.ToString(), beforeUpdate.GetProperty("media")[0].GetProperty("assetId").GetString());
        await h.Command("update");
        var afterUpdate = await client.GetFromJsonAsync<JsonElement>(h.JsonPath);
        Assert.Equal(nextId.ToString(), afterUpdate.GetProperty("media")[0].GetProperty("assetId").GetString());
    }

    [Fact]
    public async Task Unverified_or_deleted_owner_cannot_read_statistics_with_existing_cookie()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var owner = h.Client();
        await Login(owner, h.Email);
        var path = $"/api/v1/invitations/{h.Invitation}/statistics";
        await using (var db = h.Db())
        {
            (await db.Users.SingleAsync()).EmailConfirmed = false;
            await db.SaveChangesAsync();
        }
        var unverified = await owner.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, unverified.StatusCode);
        Assert.True(unverified.Headers.CacheControl?.NoStore);
        await using (var db = h.Db())
        {
            (await db.Users.SingleAsync()).EmailConfirmed = true;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invitations SET deleted_at = {Now} WHERE id = {h.Invitation}");
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("{\"contacts\":[null]}")]
    [InlineData("{\"contacts\":[{\"name\":\"Host\",\"phone\":\"javascript:alert(1)\"}]}")]
    [InlineData("{\"faqs\":[{\"question\":\"Q\"}]}")]
    [InlineData("{\"transportStops\":[{\"name\":\"Stop\",\"departureTime\":\"99:99\"}]}")]
    public async Task Malformed_optional_Published_modules_fail_closed_in_JSON_HTML_and_image(string malformed)
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await using (var db = h.Db())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE published_contents SET content = content || CAST({malformed} AS jsonb)");
        using var client = h.Client();
        var json = await client.GetAsync(h.JsonPath);
        Assert.Equal("{\"status\":\"unavailable\"}", await json.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Published headline", await client.GetStringAsync(h.HtmlPath), StringComparison.Ordinal);
        var genericImage = await client.GetByteArrayAsync("/api/v1/public/og-image.png");
        Assert.Equal(genericImage, await client.GetByteArrayAsync(h.ImagePath));
        await Render(client, h.ViewsPath);
        Assert.Equal(0, await h.Total());
    }

    [Fact]
    public async Task Views_count_only_anonymous_Active_render_events_without_identifiers_or_cookies()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var client = h.Client();
        await client.GetAsync(h.JsonPath);
        await client.GetAsync(h.HtmlPath);
        await client.GetAsync(h.ImagePath);
        await client.PostAsync(h.ViewsPath, null);
        Assert.Equal(0, await h.Total());
        foreach (var agent in new[] { "Googlebot", "facebookexternalhit", "WhatsApp", "preview" })
            Assert.Equal(HttpStatusCode.NoContent, (await Render(client, h.ViewsPath, agent)).StatusCode);
        Assert.Equal(0, await h.Total());
        var counted = await Render(client, h.ViewsPath);
        Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
        NoCache(counted);
        Assert.False(counted.Headers.Contains("Set-Cookie"));
        Assert.Equal("", await counted.Content.ReadAsStringAsync());
        Assert.Equal(1, await h.Total());
        using var owner = h.Client();
        await Login(owner, h.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await Render(owner, h.ViewsPath)).StatusCode);
        Assert.Equal(1, await h.Total());
        await h.Command("pause");
        await Render(client, h.ViewsPath);
        Assert.Equal(1, await h.Total());
        Assert.Equal(HttpStatusCode.NotFound, (await Render(client, "/api/v1/public/invitations/" + new CryptographicPublicCodeGenerator().Generate() + "/views")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_render_events_increment_atomically_and_purge_cascades_aggregate()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var client = h.Client();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Render(client, h.ViewsPath)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Equal(12, await h.Total());
        await using var db = h.Db();
        db.Invitations.Remove(await db.Invitations.SingleAsync());
        await db.SaveChangesAsync();
        Assert.False(await db.InvitationViewTotals.AnyAsync());
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Scheduled")]
    [InlineData("Paused")]
    [InlineData("Expired")]
    [InlineData("Deleted")]
    public async Task Inactive_or_deleted_render_events_never_increment(string state)
    {
        await using var h = await CreateAsync();
        if (state != "Draft") await h.Publish(state == "Scheduled");
        if (state == "Paused") await h.Command("pause");
        if (state == "Expired") h.Clock.Current = Now.AddDays(1);
        if (state == "Deleted")
        {
            await using var db = h.Db();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invitations SET deleted_at = {Now}, purge_after = {Now.AddDays(3)} WHERE id = {h.Invitation}");
        }
        using var client = h.Client();
        Assert.Equal(state == "Deleted" ? HttpStatusCode.NotFound : HttpStatusCode.NoContent, (await Render(client, h.ViewsPath)).StatusCode);
        Assert.Equal(0, await h.Total());
    }

    [Fact]
    public async Task Statistics_are_owner_only_and_account_gated_without_public_code_authority()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var anonymous = h.Client();
        await Render(anonymous, h.ViewsPath);
        var path = $"/api/v1/invitations/{h.Invitation}/statistics";
        using var anonymousStatistics = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatistics.StatusCode);
        Assert.True(anonymousStatistics.Headers.CacheControl?.NoStore);
        using var foreign = h.Client();
        await Login(foreign, await h.AddUser());
        using var foreignStatistics = await foreign.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, foreignStatistics.StatusCode);
        Assert.True(foreignStatistics.Headers.CacheControl?.NoStore);
        using var owner = h.Client();
        await Login(owner, h.Email);
        var own = await owner.GetAsync(path);
        Assert.True(own.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("totalPageViews").GetInt64());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.Equal(0, json.RootElement.GetProperty("rsvpResponseCount").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("participantCountTotal").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("memoryCount").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("readyMediaCount").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("activeGiftReservationCount").GetInt64());
        await using var db = h.Db();
        db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), h.Account, "Statistics ban", Now, Guid.NewGuid()));
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Creator_statistics_return_only_invitation_scoped_aggregates_and_exclude_stale_or_unready_rows()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        var otherInvitation = Guid.NewGuid();
        var configuration = RsvpConfiguration.Create(Guid.NewGuid(), h.Invitation, Now);
        var archivedCount = configuration.AddQuestion(Guid.NewGuid(), "Old participant count", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
        var archivedResponse = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, Now);
        archivedResponse.AddAnswer(Guid.NewGuid(), configuration, archivedCount.Id, Now, numberValue: 11m);
        configuration.ArchiveQuestion(archivedCount.Id, Now.AddMinutes(1));
        var activeCount = configuration.AddQuestion(Guid.NewGuid(), "Current participant count", RsvpQuestionType.Number,
            true, 0, null, Now.AddMinutes(1));
        var staleSnapshotResponse = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, Now.AddMinutes(2));
        staleSnapshotResponse.AddAnswer(Guid.NewGuid(), configuration, activeCount.Id, Now.AddMinutes(2), numberValue: 13m);
        configuration.UpdateQuestion(activeCount.Id, "Current participant count", RsvpQuestionType.Number,
            true, RsvpQuestionSemanticRole.ParticipantCount, Now.AddMinutes(2));
        var unrelatedNumber = configuration.AddQuestion(Guid.NewGuid(), "Table number", RsvpQuestionType.Number,
            false, 1, null, Now.AddMinutes(2));
        var guestName = configuration.AddQuestion(Guid.NewGuid(), "Name", RsvpQuestionType.ShortText,
            true, 2, null, Now.AddMinutes(2));
        var firstResponse = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, Now.AddMinutes(3));
        firstResponse.AddAnswer(Guid.NewGuid(), configuration, activeCount.Id, Now.AddMinutes(3), numberValue: 2m);
        firstResponse.AddAnswer(Guid.NewGuid(), configuration, unrelatedNumber.Id, Now.AddMinutes(3), numberValue: 99m);
        firstResponse.AddAnswer(Guid.NewGuid(), configuration, guestName.Id, Now.AddMinutes(3), textValue: "private@example.test");
        var secondResponse = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, Now.AddMinutes(4));
        secondResponse.AddAnswer(Guid.NewGuid(), configuration, activeCount.Id, Now.AddMinutes(4), numberValue: 3m);

        var publishedMemory = Memory.Create(Guid.NewGuid(), h.Invitation, "Guest PII", "Hello", null, false, Now);
        var hiddenMemory = Memory.Create(Guid.NewGuid(), h.Invitation, "Guest PII", "Hidden", null, false, Now);
        hiddenMemory.Hide(Now.AddMinutes(1));
        var pendingMemory = Memory.Create(Guid.NewGuid(), h.Invitation, "Pending", null, null, true, Now);
        var abandonedMemory = Memory.Create(Guid.NewGuid(), h.Invitation, "Abandoned", null, null, true, Now);
        abandonedMemory.Abandon();

        var creatorMedia = ReadyImage(h.Invitation, Guid.NewGuid());
        var guestMedia = ReadyGuestImage(h.Invitation, Guid.NewGuid());
        var pendingMedia = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), h.Invitation, MediaKind.Image, Now);
        pendingMedia.BeginProcessing("pending-asset");
        var rejectedMedia = MediaAsset.CreateGuestAsset(Guid.NewGuid(), h.Invitation, MediaKind.Image, Now);
        rejectedMedia.BeginProcessing("rejected-asset");
        rejectedMedia.Reject();

        var giftItem = GiftItem.Create(Guid.NewGuid(), h.Invitation, "Gift", 10, 0, Now);
        var giftSession = GuestGiftSession.Create(Guid.NewGuid(), h.Invitation, GuestGiftSession.RequiredPurpose,
            1, RandomNumberGenerator.GetBytes(GuestGiftSession.HmacSha256DigestLength), Now);
        var reservations = new[]
        {
            GiftReservation.Create(Guid.NewGuid(), h.Invitation, giftItem.Id, giftSession.Id, 1, "Private Guest One", "guest1@example.test", null, Now),
            GiftReservation.Create(Guid.NewGuid(), h.Invitation, giftItem.Id, giftSession.Id, 2, "Private Guest Two", null, "+905551234567", Now)
        };
        var otherGiftItem = GiftItem.Create(Guid.NewGuid(), otherInvitation, "Other gift", 1, 0, Now);
        var otherGiftSession = GuestGiftSession.Create(Guid.NewGuid(), otherInvitation, GuestGiftSession.RequiredPurpose,
            1, RandomNumberGenerator.GetBytes(GuestGiftSession.HmacSha256DigestLength), Now);
        var otherReservation = GiftReservation.Create(Guid.NewGuid(), otherInvitation, otherGiftItem.Id,
            otherGiftSession.Id, 1, "Other invitation", null, null, Now);

        await using (var db = h.Db())
        {
            db.RsvpConfigurations.Add(configuration);
            db.RsvpSubmissions.AddRange(archivedResponse, staleSnapshotResponse, firstResponse, secondResponse);
            db.Memories.AddRange(publishedMemory, hiddenMemory, pendingMemory, abandonedMemory);
            db.MediaAssets.AddRange(creatorMedia, guestMedia, pendingMedia, rejectedMedia);
            db.GiftItems.AddRange(giftItem, otherGiftItem);
            db.GuestGiftSessions.AddRange(giftSession, otherGiftSession);
            db.GiftReservations.AddRange(reservations);
            db.GiftReservations.Add(otherReservation);
            await db.SaveChangesAsync();

            // Representative density: the queried invitation owns 5% of RSVP rows among many
            // other invitations. The composite invitation index should keep this aggregate scoped.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO rsvp_submissions (id, invitation_id, submitted_at, updated_at, revision)
                SELECT gen_random_uuid(),
                    CASE WHEN value % 20 = 0 THEN {h.Invitation} ELSE gen_random_uuid() END,
                    {Now}, {Now}, 0
                FROM generate_series(1, 10000) AS value
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE rsvp_submissions");

            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            try
            {
                await using var explain = connection.CreateCommand();
                explain.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) " + InvitationStatisticsReader.StatisticsSql;
                explain.Parameters.Add(new NpgsqlParameter("invitation_id", h.Invitation));
                explain.Parameters.Add(new NpgsqlParameter("participant_count_role", (int)RsvpQuestionSemanticRole.ParticipantCount));
                explain.Parameters.Add(new NpgsqlParameter("number_type", (int)RsvpQuestionType.Number));
                await using var plan = await explain.ExecuteReaderAsync();
                var planLines = new List<string>();
                while (await plan.ReadAsync()) planLines.Add(plan.GetString(0));
                Assert.Contains("ix_rsvp_submissions_invitation_submitted_at", string.Join(Environment.NewLine, planLines), StringComparison.Ordinal);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        using var owner = h.Client();
        await Login(owner, h.Email);
        var stopwatch = Stopwatch.StartNew();
        var response = await owner.GetAsync($"/api/v1/invitations/{h.Invitation}/statistics");
        stopwatch.Stop();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Aggregate request took {stopwatch.Elapsed} for 10,000 representative RSVP rows.");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(0, root.GetProperty("totalPageViews").GetInt64());
        Assert.Equal(504, root.GetProperty("rsvpResponseCount").GetInt64());
        Assert.Equal(5, root.GetProperty("participantCountTotal").GetInt64());
        Assert.Equal(2, root.GetProperty("memoryCount").GetInt64());
        Assert.Equal(2, root.GetProperty("readyMediaCount").GetInt64());
        Assert.Equal(2, root.GetProperty("activeGiftReservationCount").GetInt64());
        Assert.Equal(new[]
        {
            "totalPageViews", "rsvpResponseCount", "participantCountTotal",
            "memoryCount", "readyMediaCount", "activeGiftReservationCount"
        }.Order(StringComparer.Ordinal), root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Guest PII", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Guest", body, StringComparison.Ordinal);
        Assert.DoesNotContain("guest1@example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("+905551234567", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_metadata_changes_with_gate_and_escapes_Published_text_ignoring_Host()
    {
        await using var h = await CreateAsync(Content.Replace("Published headline", "<script>alert('x')</script> & title") + "");
        await h.Publish();
        using var client = h.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, h.HtmlPath);
        request.Headers.Host = "spoofed.test";
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoCache(response);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("https://davetiye.example.test/davetiye/" + h.Code, html, StringComparison.Ordinal);
        Assert.DoesNotContain("spoofed.test", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE FULL ADDRESS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale shell title", html, StringComparison.Ordinal);
        Assert.Contains("/assets/app.js", html, StringComparison.Ordinal);
        await h.Command("pause");
        var unavailable = await client.GetAsync(h.HtmlPath);
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        NoCache(unavailable);
        Assert.DoesNotContain("alert", await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var db = h.Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invitations SET deleted_at = {Now} WHERE id = {h.Invitation}");
        var deleted = await client.GetAsync(h.HtmlPath);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        NoCache(deleted);
        Assert.DoesNotContain("alert", await deleted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_shell_await_crossing_window_end_reads_fresh_gate_before_metadata()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        h.Shell.BeforeReturn = () => h.Clock.Current = Now.AddDays(1);
        using var client = h.Client();
        var response = await client.GetAsync(h.HtmlPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Published headline", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await h.Total());
    }

    [Fact]
    public async Task Og_images_are_local_png_gate_sensitive_and_never_count_views()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var client = h.Client();
        var active = await client.GetAsync(h.ImagePath);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        NoCache(active);
        Assert.Equal("image/png", active.Content.Headers.ContentType!.MediaType);
        var activeBytes = await active.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, activeBytes[..4]);
        await h.Command("pause");
        var paused = await client.GetAsync(h.ImagePath);
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        var pausedBytes = await paused.Content.ReadAsByteArrayAsync();
        Assert.False(activeBytes.SequenceEqual(pausedBytes));
        Assert.Equal(0, await h.Total());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/public/invitations/invalid/og-image.png")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("public-embed-key")]
    public async Task Maps_capabilities_use_exact_server_allowlist_and_do_not_contact_third_party(string? key)
    {
        await using var h = await CreateAsync(mapKey: key);
        using var client = h.Client();
        var response = await client.GetAsync("/api/v1/public/capabilities");
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("https://www.google.com", json.RootElement.GetProperty("mapEmbedOrigin").GetString());
        Assert.Equal("/maps/embed/v1/place", json.RootElement.GetProperty("mapEmbedPath").GetString());
        Assert.Equal(key is not null, json.RootElement.GetProperty("mapEmbedEnabled").GetBoolean());
        Assert.Equal("https://davetiye.example.test", json.RootElement.GetProperty("canonicalBaseUrl").GetString());
        Assert.Equal(0, h.Shell.Requests);
    }

    private static void NoCache(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")), StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.Headers.ETag);
    }
    private static Task<HttpResponseMessage> Render(HttpClient client, string path, string agent = "Mozilla/5.0")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Invitation-Render", "1");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.UserAgent.ParseAdd(agent);
        return client.SendAsync(request);
    }
    private static async Task Login(HttpClient client, string email)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/antiforgery/token"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }
    private async Task<Harness> CreateAsync(string content = Content, string? mapKey = null, int renderer = 1)
    {
        var h = new Harness(new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false }.ConnectionString, mapKey);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        await h.AddUser(h.Email, h.Account);
        var invitation = Invitation.Create(h.Invitation, h.Account, h.Code, Now);
        invitation.PinTemplate("zamansiz-dugun", renderer);
        db.Invitations.Add(invitation);
        db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), h.Invitation, 1, content, Now));
        await db.SaveChangesAsync();
        return h;
    }

    private static MediaAsset ReadyImage(Guid invitationId, Guid id)
    {
        var reference = $"private-{id:N}";
        var asset = MediaAsset.CreateCreatorAsset(id, invitationId, MediaKind.Image, Now);
        asset.BeginProcessing(reference);
        asset.MarkReady(new NormalizedImageVerificationEvidence(reference, "image/webp", 512), Now.AddSeconds(1));
        return asset;
    }
    private static MediaAsset ReadyGuestImage(Guid invitationId, Guid id)
    {
        var reference = $"guest-private-{id:N}";
        var asset = MediaAsset.CreateGuestAsset(id, invitationId, MediaKind.Image, Now);
        asset.BeginProcessing(reference);
        asset.MarkReady(new NormalizedImageVerificationEvidence(reference, "image/webp", 512), Now.AddSeconds(1));
        return asset;
    }
    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }
    private sealed class ShellFactory : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests { get; private set; }
        public Action? BeforeReturn { get; set; }
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Assert.Equal("http://127.0.0.1:5173/", request.RequestUri!.ToString());
            BeforeReturn?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><head><title>Stale shell title</title><meta property='og:title' content='stale'></head><body><div id='root'></div><script src='/assets/app.js'></script></body></html>") });
        }
    }
    private sealed class Harness(string connection, string? mapKey) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Code { get; } = new CryptographicPublicCodeGenerator().Generate();
        public string Email { get; } = $"experience-{Guid.NewGuid():N}@example.test";
        public string JsonPath => $"/api/v1/public/invitations/{Code}";
        public string HtmlPath => $"/davetiye/title-{Code}";
        public string ImagePath => JsonPath + "/og-image.png";
        public string ViewsPath => JsonPath + "/views";
        public MutableClock Clock { get; } = new();
        public ShellFactory Shell { get; } = new();
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connection, options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection, ["PublicWeb:BaseUrl"] = "https://davetiye.example.test", ["PublicWeb:AppShellUrl"] = "http://127.0.0.1:5173/", ["PublicMaps:GoogleEmbedApiKey"] = mapKey, ["InvitationLifecycleJobs:Enabled"] = "false", ["AuthRateLimits:PublicInvitationRead:PermitLimit"] = "100"
            }));
            builder.ConfigureTestServices(services => { services.AddSingleton<IClock>(Clock); services.AddSingleton<IHttpClientFactory>(Shell); });
        }
        public async Task<string> AddUser(string? email = null, Guid? account = null)
        {
            email ??= $"foreign-{Guid.NewGuid():N}@example.test";
            var accountId = account ?? Guid.NewGuid();
            await using var db = Db();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Experience tester", Now));
            db.AccountConsentRecords.Add(AccountConsentRecord.Create(Guid.NewGuid(), accountId,
                AccountConsentKind.ServiceNoticeAcknowledgement, true, AccountConsentVersions.ServiceNotice,
                AccountConsentSource.ExistingAccountAcknowledgement, Now));
            await db.SaveChangesAsync();
            return email;
        }
        public async Task Publish(bool scheduled = false)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, Invitation, new("publish", status.Expected,
                new(scheduled ? "Scheduled" : "Immediate", scheduled ? "2026-10-03T15:00:00" : null, scheduled ? "2026-10-04T15:00:00" : "2026-10-03T15:00:00", "Europe/Istanbul", null), ProceedWithRecommendedWarnings: true), default)).Code);
        }
        public async Task Command(string action)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, Invitation, new(action, status.Expected), default)).Code);
        }
        public async Task<long> Total()
        {
            await using var db = Db();
            return await db.InvitationViewTotals.Select(item => (long?)item.Total).SingleOrDefaultAsync() ?? 0;
        }
    }
}
