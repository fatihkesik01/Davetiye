using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Media;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>P6-M2: public text/emoji Memories surface and Creator configuration, over real PostgreSQL.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class MemoriesEndpointsTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "TestPassw0rd1";
    private const string Origin = "https://allowed.example.test";
    private const string Content = """{"eventType":"dugun","headline":"Memories headline","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Venue","address":"Address"},"message":"Welcome","hostNames":["Ada"],"programItems":[{"title":"Reception","startsAt":"2026-10-10T13:00:00Z"}]}""";

    [Theory]
    [InlineData("draft")]
    [InlineData("scheduled")]
    [InlineData("paused")]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("entitlement-off")]
    public async Task Gates_that_fail_make_every_public_surface_uniformly_unavailable(string gate)
    {
        await using var h = await CreateAsync();
        if (gate != "draft") await h.Publish(scheduled: gate == "scheduled", paid: gate != "entitlement-off");
        await h.SeedConfiguration(enabled: gate != "disabled", MemoryVisibility.Public);
        await h.SeedMemory(MemoryState.Published, "pre-existing");
        var deliveryMemoryId = Guid.NewGuid();
        var deliveryAssetId = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(deliveryMemoryId,
            (deliveryAssetId, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation));
        if (gate == "paused") await h.Command("pause");
        if (gate == "expired") h.Clock.Current = Now.AddDays(30);

        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path + "/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path)).StatusCode);
        using (var delivery = await PostMediaDelivery(guest, $"{path}/{deliveryMemoryId}/media/{deliveryAssetId}/delivery"))
            Assert.Equal(HttpStatusCode.NotFound, delivery.StatusCode);
        Assert.Equal(0, h.FakeGateway.ImageCalls);
        var csrf = await Csrf(guest);
        using var submit = await Post(guest, path, new { text = "hello" }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        await using var db = h.Db();
        Assert.Equal(2, await db.Memories.CountAsync());
    }

    [Fact]
    public async Task Unknown_or_malformed_code_is_404()
    {
        await using var h = await CreateAsync();
        using var guest = h.CreateGuest();
        var csrf = await Csrf(guest);
        foreach (var code in new[] { "nope", new string((char)97, 64), "a.b" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/invitations/{code}/memories")).StatusCode);
            using var response = await Post(guest, $"/api/v1/public/invitations/{code}/memories", new { text = "x" }, csrf);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Active_enabled_entitled_invitation_accepts_anonymous_text_and_emoji_without_identity_or_cookies()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";

        using (var config = await guest.GetAsync(path + "/configuration"))
        {
            Assert.Equal(HttpStatusCode.OK, config.StatusCode);
            AssertPrivateHeaders(config);
            using var json = JsonDocument.Parse(await config.Content.ReadAsStringAsync());
            Assert.Equal("available", json.RootElement.GetProperty("status").GetString());
            var limits = json.RootElement.GetProperty("limits");
            Assert.Equal(60, limits.GetProperty("maxDisplayNameCharacters").GetInt32());
            Assert.Equal(500, limits.GetProperty("maxTextCharacters").GetInt32());
            var uploadLimits = json.RootElement.GetProperty("uploadLimits");
            Assert.True(uploadLimits.GetProperty("enabled").GetBoolean());
            Assert.Equal(3, uploadLimits.GetProperty("maxMediaItems").GetInt32());
            Assert.Equal(100, uploadLimits.GetProperty("maxImages").GetInt64());
            Assert.Equal(10, uploadLimits.GetProperty("maxVideos").GetInt64());
            Assert.Equal(10, uploadLimits.GetProperty("maxImageSizeMb").GetInt64());
            Assert.Equal(100, uploadLimits.GetProperty("maxVideoSizeMb").GetInt64());
            Assert.Equal(60, uploadLimits.GetProperty("maxVideoDurationSeconds").GetInt64());
            Assert.Equal(new[] { "limits", "status", "uploadLimits" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }

        var csrf = await Csrf(guest);
        using var created = await Post(guest, path, new { displayName = "  Ada  ", text = "Harika bir gece\r\nTesekkurler", emoji = "🎉" }, csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertPrivateHeaders(created);
        Assert.False(created.Headers.Contains("Set-Cookie") && created.Headers.GetValues("Set-Cookie").Any(c => c.Contains("memor", StringComparison.OrdinalIgnoreCase)));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "createdAt", "memoryId" }, createdJson.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());

        await using (var db = h.Db())
        {
            var stored = await db.Memories.SingleAsync();
            Assert.Equal(MemoryState.Published, stored.State);
            Assert.Equal("Ada", stored.DisplayName);
            Assert.Equal("Harika bir gece\nTesekkurler", stored.Text);
            Assert.Equal("🎉", stored.Emoji);
            Assert.Equal(h.Invitation, stored.InvitationId);
        }

        using var list = await guest.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        AssertPrivateHeaders(list);
        var body = await list.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(body);
        var item = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "createdAt", "displayName", "emoji", "id", "media", "text" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        foreach (var forbidden in new[] { "invitationId", "accountId", "state", "revision", "hiddenAt", "finalizedAt", "ip", "userAgent" })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(document.cookie)>")]
    [InlineData("\"><svg/onload=alert(1)>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("{{7*7}} ${7*7} '; DROP TABLE memories;--")]
    public async Task Stored_xss_payloads_are_stored_as_inert_text_and_returned_json_escaped(string payload)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        using var created = await Post(guest, path, new { displayName = payload.Length <= 60 ? payload : payload[..60], text = payload }, await Csrf(guest));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using (var db = h.Db()) Assert.Equal(payload, (await db.Memories.SingleAsync()).Text);

        using var list = await guest.GetAsync(path);
        var raw = await list.Content.ReadAsStringAsync();
        Assert.Equal("application/json", list.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(">", raw, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(list.Headers.GetValues("X-Content-Type-Options")));
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(payload, json.RootElement.GetProperty("items")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("text", "a\u0000b")]
    [InlineData("text", "bell\u0007")]
    [InlineData("text", "\u202Eevil")]
    [InlineData("text", "x\u2066y\u2069")]
    [InlineData("text", "x\u200Fy")]
    [InlineData("text", "x\u2028y")]
    [InlineData("text", "x\uFEFFy")]
    [InlineData("text", "tab\there")]
    [InlineData("displayName", "line\nbreak")]
    [InlineData("displayName", "\u202Dname")]
    [InlineData("displayName", "nul\u0000")]
    [InlineData("emoji", "\u202E😀")]
    public async Task Control_and_bidi_override_characters_are_rejected(string field, string value)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var body = new Dictionary<string, string?> { ["text"] = "ok" };
        body[field] = value;
        using var response = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", body, await Csrf(guest));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = h.Db();
        Assert.Empty(await db.Memories.ToListAsync());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("Ada", null, null)]
    [InlineData(null, "   ", " ")]
    [InlineData(null, "", "")]
    public async Task At_least_one_of_text_or_emoji_is_required(string? name, string? text, string? emoji)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        using var response = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories",
            new { displayName = name, text, emoji }, await Csrf(guest));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("displayName", 61, false)]
    [InlineData("displayName", 60, true)]
    [InlineData("text", 501, false)]
    [InlineData("text", 500, true)]
    public async Task Length_limits_are_enforced(string field, int length, bool accepted)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var body = new Dictionary<string, string?> { ["text"] = "ok" };
        body[field] = new string('a', length);
        using var response = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", body, await Csrf(guest));
        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("😀", true)]
    [InlineData("👨\u200D👩\u200D👧\u200D👦", true)]
    [InlineData("🇹🇷", true)]
    [InlineData("👍🏽", true)]
    [InlineData("1️⃣", true)]
    [InlineData("❤️", true)]
    [InlineData("a", false)]
    [InlineData("ab", false)]
    [InlineData("7", false)]
    [InlineData("😀😀", false)]
    [InlineData("😀a", false)]
    [InlineData("\u200D", false)]
    [InlineData("<b>", false)]
    [InlineData("🇹", false)]
    [InlineData("é", false)]
    public async Task Emoji_must_be_exactly_one_real_emoji(string emoji, bool accepted)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        using var response = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", new { emoji }, await Csrf(guest));
        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Text_is_nfc_normalized()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        using var response = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", new { text = "Café" }, await Csrf(guest));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = h.Db();
        Assert.Equal("Café", (await db.Memories.SingleAsync()).Text);
    }

    [Fact]
    public async Task Per_invitation_cap_holds_under_concurrent_submissions()
    {
        await using var h = await CreateAsync(maxMemories: 3, submissionRateLimit: 1000);
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var csrf = await Csrf(guest);
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var response = await Post(guest, path, new { text = $"memory {index}" }, csrf);
            return response.StatusCode;
        }));
        Assert.Equal(3, results.Count(code => code == HttpStatusCode.Created));
        Assert.Equal(9, results.Count(code => code == HttpStatusCode.Conflict));
        await using var db = h.Db();
        Assert.Equal(3, await db.Memories.CountAsync());
    }

    [Fact]
    public async Task Cap_ignores_abandoned_memories_but_counts_hidden_ones()
    {
        await using var h = await CreateAsync(maxMemories: 2);
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        await h.SeedMemory(MemoryState.Hidden, "hidden");
        await h.SeedMemory(MemoryState.Abandoned, "abandoned");
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        var csrf = await Csrf(guest);
        using (var first = await Post(guest, path, new { text = "one" }, csrf)) Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using (var second = await Post(guest, path, new { text = "two" }, csrf)) Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Submission_requires_allowed_origin_and_antiforgery_token()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        using (var noOrigin = await guest.PostAsJsonAsync(path, new { text = "x" }))
            Assert.Equal(HttpStatusCode.Forbidden, noOrigin.StatusCode);
        using (var badOrigin = await Post(guest, path, new { text = "x" }, await Csrf(guest), origin: "https://evil.example.test"))
            Assert.Equal(HttpStatusCode.Forbidden, badOrigin.StatusCode);
        using (var noToken = await Post(guest, path, new { text = "x" }, token: null))
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await using var db = h.Db();
        Assert.Empty(await db.Memories.ToListAsync());
    }

    [Fact]
    public async Task Submission_rate_limit_returns_429_after_configured_limit()
    {
        await using var h = await CreateAsync(submissionRateLimit: 2);
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        var csrf = await Csrf(guest);
        using (var a = await Post(guest, path, new { text = "1" }, csrf)) Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        using (var b = await Post(guest, path, new { text = "2" }, csrf)) Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        using (var c = await Post(guest, path, new { text = "3" }, csrf)) Assert.Equal(HttpStatusCode.TooManyRequests, c.StatusCode);
        await using var db = h.Db();
        Assert.Equal(2, await db.Memories.CountAsync());
    }

    [Fact]
    public async Task Per_invitation_submission_limit_returns_429_even_across_clients_and_never_for_unknown_codes()
    {
        await using var h = await CreateAsync(perInvitationLimit: 2);
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        using var a = h.CreateGuest();
        using var b = h.CreateGuest();
        var csrfA = await Csrf(a);
        var csrfB = await Csrf(b);
        // Unknown codes never touch the per-invitation limiter.
        for (var i = 0; i < 5; i++)
        {
            using var unknown = await Post(a, "/api/v1/public/invitations/" + new string((char)97, 22) + "/memories", new { text = "x" }, csrfA);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        using (var one = await Post(a, path, new { text = "1" }, csrfA)) Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        using (var two = await Post(b, path, new { text = "2" }, csrfB)) Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        using (var three = await Post(a, path, new { text = "3" }, csrfA)) Assert.Equal(HttpStatusCode.TooManyRequests, three.StatusCode);
        await using var db = h.Db();
        Assert.Equal(2, await db.Memories.CountAsync());
    }

    [Theory]
    [InlineData("2147483647", 50)]
    [InlineData("2147483647", 1)]
    [InlineData("999999999999", 20)]
    public async Task Absurd_page_numbers_return_an_empty_page_never_a_server_error(string page, int pageSize)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        await h.SeedMemory(MemoryState.Published, "one");
        using var guest = h.CreateGuest();
        using var response = await guest.GetAsync($"/api/v1/public/invitations/{h.Code}/memories?page={page}&pageSize={pageSize}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-2147483648")]
    public async Task Zero_or_negative_page_is_rejected(string page)
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();
        using var response = await guest.GetAsync($"/api/v1/public/invitations/{h.Code}/memories?page={page}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_timestamps_are_coarsened_to_the_minute_while_full_precision_stays_internal()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var precise = Now.AddSeconds(37).AddTicks(10);
        await h.SeedMemory(MemoryState.Published, "precise", precise);
        using var guest = h.CreateGuest();
        using var list = JsonDocument.Parse(await guest.GetStringAsync($"/api/v1/public/invitations/{h.Code}/memories"));
        var stamp = list.RootElement.GetProperty("items")[0].GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(Now, stamp);
        h.Clock.Current = Now.AddSeconds(41);
        using var created = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", new { text = "new" }, await Csrf(guest));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(Now, createdJson.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        await using var db = h.Db();
        Assert.Equal(precise, (await db.Memories.SingleAsync(m => m.Text == "precise")).CreatedAt);
        Assert.Equal(Now.AddSeconds(41), (await db.Memories.SingleAsync(m => m.Text == "new")).CreatedAt);
    }

    [Fact]
    public async Task Public_list_never_leaks_hidden_pending_abandoned_creator_only_or_disabled_content()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        await h.SeedMemory(MemoryState.Published, "visible-one");
        await h.SeedMemory(MemoryState.Hidden, "SECRET-hidden");
        await h.SeedMemory(MemoryState.PendingMedia, null);
        await h.SeedMemory(MemoryState.Abandoned, "SECRET-abandoned");
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";

        var body = await guest.GetStringAsync(path);
        Assert.Contains("visible-one", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", body, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(body)) Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());

        await h.SetConfiguration(enabled: true, MemoryVisibility.CreatorOnly);
        using (var creatorOnly = await guest.GetAsync(path)) Assert.Equal(HttpStatusCode.NotFound, creatorOnly.StatusCode);
        await h.SetConfiguration(enabled: false, MemoryVisibility.Public);
        using (var disabled = await guest.GetAsync(path)) Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        await h.SetConfiguration(enabled: true, MemoryVisibility.Public);
        using (var back = await guest.GetAsync(path)) Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    [Fact]
    public async Task Public_configuration_zeros_guest_upload_limits_when_the_grant_has_no_guest_media_entitlement()
    {
        await using var h = await CreateAsync();
        await h.DisableGuestMediaEntitlements();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        using var guest = h.CreateGuest();

        using var response = await guest.GetAsync($"/api/v1/public/invitations/{h.Code}/memories/configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var upload = json.RootElement.GetProperty("uploadLimits");
        Assert.False(upload.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, upload.GetProperty("maxMediaItems").GetInt32());
        foreach (var limit in new[] { "maxImages", "maxVideos", "maxImageSizeMb", "maxVideoSizeMb", "maxVideoDurationSeconds" })
            Assert.Equal(0, upload.GetProperty(limit).GetInt64());
    }

    [Fact]
    public async Task Public_list_exposes_only_linked_ready_guest_metadata_in_ordinal_order()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var readyFirst = Guid.NewGuid();
        var readySecond = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var creatorScope = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(memoryId,
            (readyFirst, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation),
            (readySecond, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation),
            (pending, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation));
        var secondMemory = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(secondMemory,
            (deleted, MediaQuotaScope.Guest, MediaAssetState.Deleted, h.Invitation),
            (creatorScope, MediaQuotaScope.Creator, MediaAssetState.Ready, h.Invitation),
            (foreign, MediaQuotaScope.Guest, MediaAssetState.Ready, Guid.NewGuid()));
        var thirdMemory = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(thirdMemory, (rejected, MediaQuotaScope.Guest, MediaAssetState.Rejected, h.Invitation));
        await h.SeedUnlinkedReadyAsset(Guid.NewGuid());

        using var guest = h.CreateGuest();
        var body = await guest.GetStringAsync($"/api/v1/public/invitations/{h.Code}/memories");
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var item = Assert.Single(items, entry => entry.GetProperty("id").GetGuid() == memoryId);
        Assert.Equal(new[] { readyFirst, readySecond }, item.GetProperty("media").EnumerateArray()
            .Select(media => media.GetProperty("assetId").GetGuid()).ToArray());
        Assert.Equal(new[] { "Image", "Image" }, item.GetProperty("media").EnumerateArray()
            .Select(media => media.GetProperty("kind").GetString()).ToArray());
        Assert.Empty(items.Single(entry => entry.GetProperty("id").GetGuid() == secondMemory).GetProperty("media").EnumerateArray());
        Assert.Empty(items.Single(entry => entry.GetProperty("id").GetGuid() == thirdMemory)
            .GetProperty("media").EnumerateArray());
        foreach (var forbidden in new[] { "providerObjectReference", "providerId", "deliveryUrl", pending.ToString(),
                     rejected.ToString(), deleted.ToString(), creatorScope.ToString(), foreign.ToString() })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_projection_shows_pending_text_immediately_ready_media_only_and_excludes_empty_pending_rows()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var earlyTextMemory = Guid.NewGuid();
        var pendingTextAsset = Guid.NewGuid();
        await h.SeedPendingMemoryWithAssets(earlyTextMemory, "Text appears before its upload is ready",
            (pendingTextAsset, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation));

        var mediaOnlyMemory = Guid.NewGuid();
        var readyAsset = Guid.NewGuid();
        var stillProcessingAsset = Guid.NewGuid();
        await h.SeedPendingMemoryWithAssets(mediaOnlyMemory, null,
            (readyAsset, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation),
            (stillProcessingAsset, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation));

        var emptyPendingMemory = Guid.NewGuid();
        var emptyPendingAsset = Guid.NewGuid();
        await h.SeedPendingMemoryWithAssets(emptyPendingMemory, null,
            (emptyPendingAsset, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation));

        var emptyPublishedMemory = Guid.NewGuid();
        var deletedPublishedAsset = Guid.NewGuid();
        await h.SeedPublishedMediaOnlyMemoryWithAssets(emptyPublishedMemory,
            (deletedPublishedAsset, MediaQuotaScope.Guest, MediaAssetState.Deleted, h.Invitation));

        using var guest = h.CreateGuest();
        var body = await guest.GetStringAsync($"/api/v1/public/invitations/{h.Code}/memories");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var earlyText = Assert.Single(items, item => item.GetProperty("id").GetGuid() == earlyTextMemory);
        Assert.Equal("Text appears before its upload is ready", earlyText.GetProperty("text").GetString());
        Assert.Empty(earlyText.GetProperty("media").EnumerateArray());
        var mediaOnly = Assert.Single(items, item => item.GetProperty("id").GetGuid() == mediaOnlyMemory);
        var projectedMedia = Assert.Single(mediaOnly.GetProperty("media").EnumerateArray());
        Assert.Equal(readyAsset, projectedMedia.GetProperty("assetId").GetGuid());
        Assert.DoesNotContain(pendingTextAsset.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(stillProcessingAsset.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(emptyPendingMemory.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(emptyPendingAsset.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(emptyPublishedMemory.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(deletedPublishedAsset.ToString(), body, StringComparison.OrdinalIgnoreCase);

        using var delivery = await PostMediaDelivery(guest,
            $"/api/v1/public/invitations/{h.Code}/memories/{mediaOnlyMemory}/media/{readyAsset}/delivery");
        Assert.Equal(HttpStatusCode.OK, delivery.StatusCode);
    }

    [Fact]
    public async Task Memory_delivery_is_scoped_to_public_published_links_and_rechecks_ready_after_provider_issue()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var memoryId = Guid.NewGuid();
        var ready = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var creatorScope = Guid.NewGuid();
        var foreignInvitation = Guid.NewGuid();
        var unlinked = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(memoryId,
            (ready, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation),
            (pending, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation),
            (rejected, MediaQuotaScope.Guest, MediaAssetState.Rejected, h.Invitation));
        var secondMemory = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(secondMemory,
            (deleted, MediaQuotaScope.Guest, MediaAssetState.Deleted, h.Invitation),
            (creatorScope, MediaQuotaScope.Creator, MediaAssetState.Ready, h.Invitation),
            (foreignInvitation, MediaQuotaScope.Guest, MediaAssetState.Ready, Guid.NewGuid()));
        var unlinkedMemoryId = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(unlinkedMemoryId);
        await h.SeedUnlinkedReadyAsset(unlinked);
        using var guest = h.CreateGuest();
        var root = $"/api/v1/public/invitations/{h.Code}/memories";

        var gateway = h.Services.GetRequiredService<FakeMemoryDeliveryGateway>();
        using (var missingOrigin = await PostMediaDelivery(guest, $"{root}/{memoryId}/media/{ready}/delivery", origin: null, includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);
        using (var disallowedOrigin = await PostMediaDelivery(guest, $"{root}/{memoryId}/media/{ready}/delivery", origin: "https://evil.example.test", includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, disallowedOrigin.StatusCode);
        Assert.Equal(0, gateway.ImageCalls);

        using (var response = await PostMediaDelivery(guest, $"{root}/{memoryId}/media/{ready}/delivery"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateHeaders(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("image", json.RootElement.GetProperty("kind").GetString());
            Assert.Equal("https://media.example.test/short-lived", json.RootElement.GetProperty("url").GetString());
            Assert.DoesNotContain("private-object", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        var invalidLinks = new[] { (memoryId, pending), (memoryId, rejected), (secondMemory, deleted),
            (secondMemory, creatorScope), (secondMemory, foreignInvitation), (unlinkedMemoryId, unlinked),
            (memoryId, Guid.NewGuid()) };
        foreach (var (ownerMemory, assetId) in invalidLinks)
        {
            using var response = await PostMediaDelivery(guest, $"{root}/{ownerMemory}/media/{assetId}/delivery");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertPrivateHeaders(response);
        }

        gateway.OnIssue = () =>
        {
            using var db = h.Db();
            db.MediaAssets.Single(asset => asset.Id == ready).RequestDeletion(Now.AddMinutes(1));
            db.SaveChanges();
        };
        using var raced = await PostMediaDelivery(guest, $"{root}/{memoryId}/media/{ready}/delivery");
        Assert.Equal(HttpStatusCode.NotFound, raced.StatusCode);
    }

    [Fact]
    public async Task Memory_delivery_returns_uniform_404_when_media_visibility_is_closed_and_503_on_provider_failure()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        var memoryId = Guid.NewGuid();
        var ready = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(memoryId, (ready, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation));
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories/{memoryId}/media/{ready}/delivery";
        var gateway = h.Services.GetRequiredService<FakeMemoryDeliveryGateway>();

        gateway.Failure = true;
        using (var unavailable = await PostMediaDelivery(guest, path))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            AssertPrivateHeaders(unavailable);
        }
        gateway.Failure = false;
        await h.SetConfiguration(enabled: true, MemoryVisibility.CreatorOnly);
        using var hidden = await PostMediaDelivery(guest, path);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        AssertPrivateHeaders(hidden);
    }

    [Fact]
    public async Task Public_list_is_paginated_newest_first_with_bounded_page_size()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        await h.SeedConfiguration(true, MemoryVisibility.Public);
        for (var i = 0; i < 5; i++) await h.SeedMemory(MemoryState.Published, $"m{i}", Now.AddMinutes(i));
        using var guest = h.CreateGuest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories";
        using var page = JsonDocument.Parse(await guest.GetStringAsync(path + "?page=2&pageSize=2"));
        Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "m2", "m1" }, page.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString()).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(path + "?pageSize=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(path + "?page=0")).StatusCode);
    }

    [Fact]
    public async Task Creator_configuration_is_owner_scoped_and_uses_optimistic_concurrency()
    {
        await using var h = await CreateAsync();
        var other = await h.CreateSecondCreatorAsync();
        using var creator = await h.LoginAsync(h.Email);
        var csrf = await Csrf(creator);
        var path = $"/api/v1/invitations/{h.Invitation}/memories/configuration";

        using (var get = await creator.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            AssertPrivateHeaders(get);
            using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("isEnabled").GetBoolean());
            Assert.Equal("CreatorOnly", json.RootElement.GetProperty("visibility").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("revision").GetInt64());
            Assert.Equal("Draft", json.RootElement.GetProperty("effectiveState").GetString());
            Assert.Equal(500, json.RootElement.GetProperty("inputLimits").GetProperty("maxMemoriesPerInvitation").GetInt32());
        }

        long rev;
        using (var update = await Put(creator, path, new { expectedRevision = 0, isEnabled = true, visibility = "Public" }, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            using var json = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("isEnabled").GetBoolean());
            Assert.Equal("Public", json.RootElement.GetProperty("visibility").GetString());
            rev = json.RootElement.GetProperty("revision").GetInt64();
            Assert.True(rev > 0);
        }
        using (var stale = await Put(creator, path, new { expectedRevision = 0, isEnabled = false, visibility = "CreatorOnly" }, csrf))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains($"\"currentRevision\":{rev}", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        foreach (var bad in new object[] { new { expectedRevision = rev, isEnabled = true, visibility = "Everyone" },
                     new { expectedRevision = rev, isEnabled = true, visibility = (string?)null },
                     new { expectedRevision = rev, isEnabled = true, visibility = "public" },
                     new { expectedRevision = -1, isEnabled = true, visibility = "Public" } })
        {
            using var invalid = await Put(creator, path, bad, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var noCsrf = await Put(creator, path, new { expectedRevision = rev, isEnabled = true, visibility = "Public" }, token: null))
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        // Creator B probing Creator A's invitation (and vice versa) gets an indistinguishable 404 and no mutation.
        using var attacker = await h.LoginAsync(other.Email);
        var attackerCsrf = await Csrf(attacker);
        using (var read = await attacker.GetAsync(path)) Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using (var write = await Put(attacker, path, new { expectedRevision = rev, isEnabled = false, visibility = "CreatorOnly" }, attackerCsrf))
            Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        using (var unknown = await attacker.GetAsync($"/api/v1/invitations/{Guid.NewGuid()}/memories/configuration"))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await using var db = h.Db();
        var stored = await db.MemoryConfigurations.SingleAsync(c => c.InvitationId == h.Invitation);
        Assert.True(stored.IsEnabled);
        Assert.Equal(MemoryVisibility.Public, stored.Visibility);
        Assert.Empty(await db.MemoryConfigurations.Where(c => c.InvitationId == other.InvitationId).ToListAsync());

        using var anonymous = h.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Creator_can_toggle_configuration_while_invitation_is_active_and_it_takes_effect_publicly()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        using var creator = await h.LoginAsync(h.Email);
        var csrf = await Csrf(creator);
        var path = $"/api/v1/invitations/{h.Invitation}/memories/configuration";
        using (var enable = await Put(creator, path, new { expectedRevision = 0, isEnabled = true, visibility = "Public" }, csrf))
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        using var guest = h.CreateGuest();
        using (var created = await Post(guest, $"/api/v1/public/invitations/{h.Code}/memories", new { text = "hi" }, await Csrf(guest)))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var hide = await Put(creator, path, new { expectedRevision = 2, isEnabled = true, visibility = "CreatorOnly" }, csrf))
            Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/invitations/{h.Code}/memories")).StatusCode);
        await using var db = h.Db();
        Assert.Equal(1, await db.Memories.CountAsync());
    }

    [Fact]
    public async Task Creator_configuration_rate_limit_returns_429()
    {
        await using var h = await CreateAsync(creatorReadAccountLimit: 1);
        using var creator = await h.LoginAsync(h.Email);
        var path = $"/api/v1/invitations/{h.Invitation}/memories/configuration";
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await creator.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Creator_moderation_lists_only_finalized_rows_hides_terminally_and_permanently_deletes_hidden_with_media()
    {
        await using var h = await CreateAsync();
        var other = await h.CreateSecondCreatorAsync();
        var publishedId = Guid.NewGuid();
        var publishedAsset = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(publishedId,
            (publishedAsset, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation));
        var hiddenId = Guid.NewGuid();
        var deletedAsset = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(hiddenId,
            (deletedAsset, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation));
        await h.HideMemory(hiddenId);
        await h.SeedCapability(hiddenId);
        var uploadExpiresAt = Now.AddMinutes(5);
        await h.SeedOpenUploadIntent(deletedAsset, uploadExpiresAt);
        await h.SeedMemory(MemoryState.PendingMedia, null);
        await h.SeedMemory(MemoryState.Abandoned, null);

        using var creator = await h.LoginAsync(h.Email);
        using var attacker = await h.LoginAsync(other.Email);
        var root = $"/api/v1/invitations/{h.Invitation}/memories";
        using (var list = await creator.GetAsync(root))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            AssertPrivateHeaders(list);
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var states = items.Select(item => item.GetProperty("state").GetString()).ToArray();
            Assert.Contains("Hidden", states);
            Assert.Contains("Published", states);
            var media = Assert.Single(items.Single(item => item.GetProperty("id").GetGuid() == publishedId)
                .GetProperty("media").EnumerateArray());
            Assert.Equal(publishedAsset, media.GetProperty("assetId").GetGuid());
            Assert.Equal("Ready", media.GetProperty("status").GetString());
            Assert.DoesNotContain("providerObjectReference", await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync(root)).StatusCode);

        var creatorCsrf = await Csrf(creator);
        var attackCsrf = await Csrf(attacker);
        using (var foreignHide = await EmptyRequest(attacker, HttpMethod.Put, $"{root}/{publishedId}/hide", attackCsrf))
            Assert.Equal(HttpStatusCode.NotFound, foreignHide.StatusCode);
        using (var hide = await EmptyRequest(creator, HttpMethod.Put, $"{root}/{publishedId}/hide", creatorCsrf))
            Assert.Equal(HttpStatusCode.NoContent, hide.StatusCode);
        using (var repeatHide = await EmptyRequest(creator, HttpMethod.Put, $"{root}/{publishedId}/hide", creatorCsrf))
            Assert.Equal(HttpStatusCode.Conflict, repeatHide.StatusCode);
        using (var foreignDelete = await EmptyRequest(attacker, HttpMethod.Delete, $"{root}/{hiddenId}", attackCsrf))
            Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        using (var delete = await EmptyRequest(creator, HttpMethod.Delete, $"{root}/{hiddenId}", creatorCsrf))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await using var db = h.Db();
        Assert.Equal(MemoryState.Hidden, (await db.Memories.SingleAsync(memory => memory.Id == publishedId)).State);
        Assert.False(await db.Memories.AnyAsync(memory => memory.Id == hiddenId));
        Assert.False(await db.MemoryMedia.AnyAsync(link => link.MemoryId == hiddenId));
        Assert.False(await db.MemoryUploadCapabilities.AnyAsync(capability => capability.MemoryId == hiddenId));
        Assert.Equal(MediaAssetState.PendingDeletion, (await db.MediaAssets.SingleAsync(asset => asset.Id == deletedAsset)).State);
        var cancelledIntent = await db.PendingUploads.SingleAsync(intent => intent.MediaAssetId == deletedAsset);
        Assert.NotNull(cancelledIntent.CancelledAt);
        var deletion = await db.OutboxMessages.SingleAsync(message =>
            message.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion &&
            message.Id == MediaPurgeCoordinator.StableMessageId(deletedAsset));
        Assert.Contains(deletedAsset.ToString("N"), deletion.Payload, StringComparison.OrdinalIgnoreCase);

        // The issued direct-upload URL may remain usable after its intent is cancelled. The deletion outbox waits for expiry,
        // then retries transient provider failures through the existing queue.
        await using var lifecycleScope = h.Services.CreateAsyncScope();
        var lifecycle = lifecycleScope.ServiceProvider.GetRequiredService<IMediaLifecycleJobs>();
        h.Maintenance.Failure = true;
        await lifecycle.RunBatchAsync(100, default);
        Assert.Equal(0, h.Maintenance.DeleteCalls);
        await using (var deferCheck = h.Db())
        {
            var deferred = await deferCheck.OutboxMessages.SingleAsync(message => message.Id == deletion.Id);
            Assert.Equal(uploadExpiresAt, deferred.NextAttemptAt);
        }
        h.Clock.Current = uploadExpiresAt.AddSeconds(1);
        await lifecycle.RunBatchAsync(100, default);
        Assert.Equal(1, h.Maintenance.DeleteCalls);
        h.Maintenance.Failure = false;
        h.Clock.Current = h.Clock.Current.AddSeconds(15);
        await lifecycle.RunBatchAsync(100, default);
        Assert.Equal(2, h.Maintenance.DeleteCalls);
        await using var finalDb = h.Db();
        Assert.NotNull((await finalDb.OutboxMessages.SingleAsync(message => message.Id == deletion.Id)).ProcessedAt);
        Assert.Equal(MediaAssetState.Deleted, (await finalDb.MediaAssets.SingleAsync(asset => asset.Id == deletedAsset)).State);
    }

    [Fact]
    public async Task Creator_memory_preview_is_private_ready_only_and_rechecks_after_provider_issue()
    {
        await using var h = await CreateAsync();
        var other = await h.CreateSecondCreatorAsync();
        var memoryId = Guid.NewGuid();
        var ready = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        await h.SeedPublishedMemoryWithAssets(memoryId,
            (ready, MediaQuotaScope.Guest, MediaAssetState.Ready, h.Invitation),
            (pending, MediaQuotaScope.Guest, MediaAssetState.Processing, h.Invitation),
            (deleted, MediaQuotaScope.Guest, MediaAssetState.Deleted, h.Invitation));
        await h.HideMemory(memoryId);
        using var creator = await h.LoginAsync(h.Email);
        using var attacker = await h.LoginAsync(other.Email);
        var root = $"/api/v1/invitations/{h.Invitation}/memories/{memoryId}/media";
        using (var foreign = await PostMediaDelivery(attacker, $"{root}/{ready}/delivery"))
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var pendingResponse = await PostMediaDelivery(creator, $"{root}/{pending}/delivery"))
            Assert.Equal(HttpStatusCode.NotFound, pendingResponse.StatusCode);
        using (var deletedResponse = await PostMediaDelivery(creator, $"{root}/{deleted}/delivery"))
            Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        using (var response = await PostMediaDelivery(creator, $"{root}/{ready}/delivery"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateHeaders(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("image", json.RootElement.GetProperty("mediaKind").GetString());
            Assert.Equal("https://media.example.test/short-lived", json.RootElement.GetProperty("deliveryUrl").GetString());
            Assert.True(json.RootElement.GetProperty("expiresAt").GetDateTimeOffset() > h.Clock.UtcNow);
        }

        var gateway = h.Services.GetRequiredService<FakeMemoryDeliveryGateway>();
        gateway.Failure = true;
        using (var providerFailure = await PostMediaDelivery(creator, $"{root}/{ready}/delivery"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, providerFailure.StatusCode);
        gateway.Failure = false;
        gateway.OnIssue = () => h.MarkAssetPendingDeletion(ready).GetAwaiter().GetResult();
        using (var revoked = await PostMediaDelivery(creator, $"{root}/{ready}/delivery"))
            Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        gateway.OnIssue = null;
    }

    private static void AssertPrivateHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.ETag);
        Assert.Contains("no-referrer", response.Headers.GetValues("Referrer-Policy"));
    }

    private sealed record CsrfResponse(string Token);

    private static async Task<string> Csrf(HttpClient client) =>
        (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token"))!.Token;

    private static async Task<HttpResponseMessage> PostMediaDelivery(HttpClient client, string path,
        string? origin = Origin, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (includeCsrf) request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? token,
        string origin = Origin) => Send(HttpMethod.Post, client, path, body, token, origin);

    private static Task<HttpResponseMessage> Put(HttpClient client, string path, object body, string? token) =>
        Send(HttpMethod.Put, client, path, body, token, Origin);

    private static async Task<HttpResponseMessage> EmptyRequest(HttpClient client, HttpMethod method, string path,
        string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        if (token is not null) request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Send(HttpMethod method, HttpClient client, string path, object body,
        string? token, string origin)
    {
        using var message = new HttpRequestMessage(method, path)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        message.Headers.TryAddWithoutValidation("Origin", origin);
        if (token is not null) message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        return await client.SendAsync(message);
    }

    private async Task<Harness> CreateAsync(int? maxMemories = null, int? submissionRateLimit = null, int? creatorReadAccountLimit = null, int? perInvitationLimit = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false }.ConnectionString;
        var h = new Harness(connection, maxMemories, submissionRateLimit, creatorReadAccountLimit, perInvitationLimit);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        await h.AddCreatorAsync(db, h.Account, h.Invitation, h.Code, h.Email);
        await db.SaveChangesAsync();
        return h;
    }

    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }

    private sealed class Harness(string connection, int? maxMemories, int? submissionRateLimit, int? creatorReadAccountLimit,
        int? perInvitationLimit)
        : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Code { get; } = new CryptographicPublicCodeGenerator().Generate();
        public string Email { get; } = $"memories-{Guid.NewGuid():N}@example.test";
        public MutableClock Clock { get; } = new();
        public FakeGuestMediaUploadAvailability MediaAvailability { get; } = new(true);
        public FakeMediaMaintenance Maintenance { get; } = new();

        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

        public HttpClient CreateGuest() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = Origin,
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                ["RsvpCapabilities:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
                ["RsvpCapabilities:HmacKeyVersion"] = "1",
                ["MemoryValidation:MaxMemoriesPerInvitation"] = (maxMemories ?? MemoryInputLimits.EngineeringDefaultMaxMemoriesPerInvitation).ToString(),
                ["AuthRateLimits:PublicMemorySubmission:PermitLimit"] = (submissionRateLimit ?? 200).ToString(),
                ["AuthRateLimits:PublicMemorySubmission:WindowSeconds"] = "60",
                ["AuthRateLimits:PublicMemorySubmissionPerInvitation:PermitLimit"] = (perInvitationLimit ?? 1000).ToString(),
                ["AuthRateLimits:PublicMemorySubmissionPerInvitation:WindowSeconds"] = "60",
                ["AuthRateLimits:PublicInvitationRead:PermitLimit"] = "1000",
                ["AuthRateLimits:PublicInvitationRead:WindowSeconds"] = "60",
                ["AuthRateLimits:CreatorMemoriesReadAccount:PermitLimit"] = (creatorReadAccountLimit ?? 120).ToString(),
                ["AuthRateLimits:CreatorMemoriesReadAccount:WindowSeconds"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(Clock);
                services.RemoveAll<IGuestMediaUploadAvailability>();
                services.AddSingleton<IGuestMediaUploadAvailability>(MediaAvailability);
                services.RemoveAll<IPrivateMediaDeliveryGateway>();
                services.AddSingleton(FakeGateway);
                services.AddSingleton<IPrivateMediaDeliveryGateway>(provider => provider.GetRequiredService<FakeMemoryDeliveryGateway>());
                services.RemoveAll<IMediaProviderAssetMaintenance>();
                services.AddSingleton<IMediaProviderAssetMaintenance>(Maintenance);
            });
        }

        public FakeMemoryDeliveryGateway FakeGateway { get; } = new();

        public async Task AddCreatorAsync(DavetiyeDbContext db, Guid accountId, Guid invitationId, string code, string email)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Memories tester", Now));
            db.AddAcknowledgedServiceNotice(accountId, Now);
            var invitation = Invitation_Create(invitationId, accountId, code);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), invitationId, 1, Content, Now));
            await Task.CompletedTask;
        }

        private static Davetiye.Domain.Modules.Invitations.Invitation Invitation_Create(Guid id, Guid accountId, string code)
        {
            var invitation = Davetiye.Domain.Modules.Invitations.Invitation.Create(id, accountId, code, Now);
            invitation.PinTemplate("zamansiz-dugun", 1);
            return invitation;
        }

        public async Task<(Guid AccountId, Guid InvitationId, string Email)> CreateSecondCreatorAsync()
        {
            var accountId = Guid.NewGuid();
            var invitationId = Guid.NewGuid();
            var email = $"memories-other-{Guid.NewGuid():N}@example.test";
            await using var db = Db();
            await AddCreatorAsync(db, accountId, invitationId, new CryptographicPublicCodeGenerator().Generate(), email);
            await db.SaveChangesAsync();
            return (accountId, invitationId, email);
        }

        public async Task<HttpClient> LoginAsync(string email)
        {
            var client = CreateGuest();
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }

        public async Task Publish(bool scheduled = false, bool paid = false)
        {
            Guid? grantId = null;
            if (paid)
            {
                await using var db = Db();
                var grant = AccountPlanGrant.Create(Guid.NewGuid(), Account, (await db.Plans.SingleAsync(p => p.Key == "premium")).Id, GrantSource.IndividualPurchase, Now);
                db.AccountPlanGrants.Add(grant);
                await db.SaveChangesAsync();
                grantId = grant.Id;
            }
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            var times = new PublicationWindowRequest(scheduled ? "Scheduled" : "Immediate", scheduled ? "2026-10-03T15:00:00" : null,
                scheduled ? "2026-10-04T15:00:00" : "2026-10-03T15:00:00", "Europe/Istanbul", grantId);
            var result = await service.ExecuteAsync(Account, Invitation, new("publish", status.Expected, times, ProceedWithRecommendedWarnings: true), default);
            Assert.Equal("Succeeded", result.Code);
        }

        public async Task Command(string action)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, Invitation, new(action, status.Expected), default)).Code);
        }

        public async Task SeedConfiguration(bool enabled, MemoryVisibility visibility)
        {
            await using var db = Db();
            var configuration = MemoryConfiguration.Create(Guid.NewGuid(), Invitation, Now);
            configuration.SetEnabled(enabled, Now);
            configuration.SetVisibility(visibility, Now);
            db.MemoryConfigurations.Add(configuration);
            await db.SaveChangesAsync();
        }

        public async Task SetConfiguration(bool enabled, MemoryVisibility visibility)
        {
            await using var db = Db();
            var configuration = await db.MemoryConfigurations.SingleAsync(c => c.InvitationId == Invitation);
            configuration.SetEnabled(enabled, Now.AddMinutes(1));
            configuration.SetVisibility(visibility, Now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        public async Task DisableGuestMediaEntitlements()
        {
            await using var db = Db();
            var plan = await db.Plans.SingleAsync(item => item.Key == "premium");
            foreach (var key in new[] { EntitlementCatalog.MaxGuestImages, EntitlementCatalog.MaxGuestVideos,
                         EntitlementCatalog.MaxGuestImageSizeMb, EntitlementCatalog.MaxGuestVideoSizeMb,
                         EntitlementCatalog.MaxGuestVideoDurationSeconds })
                (await db.PlanEntitlements.SingleAsync(item => item.PlanId == plan.Id && item.EntitlementKey == key))
                    .UpdateValue(0, null);
            await db.SaveChangesAsync();
        }

        public async Task SeedMemory(MemoryState state, string? text, DateTimeOffset? createdAt = null)
        {
            var at = createdAt ?? Now;
            var memory = Memory.Create(Guid.NewGuid(), Invitation, null, text, null, expectsMedia: state is MemoryState.PendingMedia or MemoryState.Abandoned, at);
            if (state == MemoryState.Hidden) memory.Hide(at);
            if (state == MemoryState.Abandoned) memory.Abandon();
            await using var db = Db();
            db.Memories.Add(memory);
            await db.SaveChangesAsync();
        }

        public async Task HideMemory(Guid memoryId)
        {
            await using var db = Db();
            var memory = await db.Memories.SingleAsync(item => item.Id == memoryId);
            memory.Hide(Now.AddSeconds(5));
            await db.SaveChangesAsync();
        }

        public async Task SeedCapability(Guid memoryId)
        {
            await using var db = Db();
            db.MemoryUploadCapabilities.Add(MemoryUploadCapability.Create(Guid.NewGuid(), memoryId,
                MemoryUploadCapability.RequiredPurpose, 1, new byte[MemoryUploadCapability.HmacSha256DigestLength],
                Now, Now.AddMinutes(10)));
            await db.SaveChangesAsync();
        }

        public async Task MarkAssetPendingDeletion(Guid assetId)
        {
            await using var db = Db();
            var asset = await db.MediaAssets.SingleAsync(item => item.Id == assetId);
            asset.RequestDeletion(Now.AddSeconds(6));
            await db.SaveChangesAsync();
        }

        public async Task SeedOpenUploadIntent(Guid assetId, DateTimeOffset expiresAt)
        {
            await using var db = Db();
            db.PendingUploads.Add(PendingUpload.Create(Guid.NewGuid(), assetId, Guid.NewGuid(), MediaPresentationRole.Gallery,
                100, new string('a', 64), Now, expiresAt, 1024));
            await db.SaveChangesAsync();
        }

        public async Task SeedPublishedMemoryWithAssets(Guid memoryId,
            params (Guid AssetId, MediaQuotaScope Scope, MediaAssetState State, Guid AssetInvitationId)[] assets)
        {
            await using var db = Db();
            var memory = Memory.Create(memoryId, Invitation, "Guest", "Media memory", null, expectsMedia: true, Now);
            foreach (var invitationId in assets.Select(item => item.AssetInvitationId).Where(id => id != Invitation).Distinct())
                db.Invitations.Add(Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, Account,
                    new CryptographicPublicCodeGenerator().Generate(), Now));
            for (var index = 0; index < assets.Length; index++)
            {
                var item = assets[index];
                var kind = MediaKind.Image;
                var asset = item.Scope == MediaQuotaScope.Guest
                    ? MediaAsset.CreateGuestAsset(item.AssetId, item.AssetInvitationId, kind, Now)
                    : MediaAsset.CreateCreatorAsset(item.AssetId, item.AssetInvitationId, kind, Now);
                if (item.State is MediaAssetState.Processing or MediaAssetState.Ready or MediaAssetState.Rejected or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.BeginProcessing($"object-{item.AssetId:N}");
                if (item.State is MediaAssetState.Ready or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.MarkReady(new NormalizedImageVerificationEvidence($"object-{item.AssetId:N}", "image/webp", 128), Now.AddSeconds(1));
                if (item.State == MediaAssetState.Rejected) asset.Reject();
                if (item.State is MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.RequestDeletion(Now.AddSeconds(2));
                if (item.State == MediaAssetState.Deleted) asset.ConfirmProviderDeletion(Now.AddSeconds(3));
                db.MediaAssets.Add(asset);
                if (index < MemoryInputLimits.HardMaxMediaPerMemory)
                    memory.AttachMedia(item.AssetId, Guid.NewGuid());
            }
            memory.Finalize(Now.AddSeconds(4));
            db.Memories.Add(memory);
            await db.SaveChangesAsync();
        }

        public async Task SeedPublishedMediaOnlyMemoryWithAssets(Guid memoryId,
            params (Guid AssetId, MediaQuotaScope Scope, MediaAssetState State, Guid AssetInvitationId)[] assets)
        {
            await using var db = Db();
            var memory = Memory.Create(memoryId, Invitation, "Guest", null, null, expectsMedia: true, Now);
            foreach (var invitationId in assets.Select(item => item.AssetInvitationId).Where(id => id != Invitation).Distinct())
                db.Invitations.Add(Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, Account,
                    new CryptographicPublicCodeGenerator().Generate(), Now));
            for (var index = 0; index < assets.Length; index++)
            {
                var item = assets[index];
                var asset = item.Scope == MediaQuotaScope.Guest
                    ? MediaAsset.CreateGuestAsset(item.AssetId, item.AssetInvitationId, MediaKind.Image, Now)
                    : MediaAsset.CreateCreatorAsset(item.AssetId, item.AssetInvitationId, MediaKind.Image, Now);
                if (item.State is MediaAssetState.Processing or MediaAssetState.Ready or MediaAssetState.Rejected or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.BeginProcessing($"object-{item.AssetId:N}");
                if (item.State is MediaAssetState.Ready or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.MarkReady(new NormalizedImageVerificationEvidence($"object-{item.AssetId:N}", "image/webp", 128), Now.AddSeconds(1));
                if (item.State == MediaAssetState.Rejected) asset.Reject();
                if (item.State is MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.RequestDeletion(Now.AddSeconds(2));
                if (item.State == MediaAssetState.Deleted) asset.ConfirmProviderDeletion(Now.AddSeconds(3));
                db.MediaAssets.Add(asset);
                if (index < MemoryInputLimits.HardMaxMediaPerMemory) memory.AttachMedia(item.AssetId, Guid.NewGuid());
            }
            memory.Finalize(Now.AddSeconds(4));
            db.Memories.Add(memory);
            await db.SaveChangesAsync();
        }

        public async Task SeedPendingMemoryWithAssets(Guid memoryId, string? text,
            params (Guid AssetId, MediaQuotaScope Scope, MediaAssetState State, Guid AssetInvitationId)[] assets)
        {
            await using var db = Db();
            var memory = Memory.Create(memoryId, Invitation, "Guest", text, null, expectsMedia: true, Now);
            foreach (var invitationId in assets.Select(item => item.AssetInvitationId).Where(id => id != Invitation).Distinct())
                db.Invitations.Add(Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, Account,
                    new CryptographicPublicCodeGenerator().Generate(), Now));
            for (var index = 0; index < assets.Length; index++)
            {
                var item = assets[index];
                var asset = item.Scope == MediaQuotaScope.Guest
                    ? MediaAsset.CreateGuestAsset(item.AssetId, item.AssetInvitationId, MediaKind.Image, Now)
                    : MediaAsset.CreateCreatorAsset(item.AssetId, item.AssetInvitationId, MediaKind.Image, Now);
                if (item.State is MediaAssetState.Processing or MediaAssetState.Ready or MediaAssetState.Rejected or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.BeginProcessing($"object-{item.AssetId:N}");
                if (item.State is MediaAssetState.Ready or MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.MarkReady(new NormalizedImageVerificationEvidence($"object-{item.AssetId:N}", "image/webp", 128), Now.AddSeconds(1));
                if (item.State == MediaAssetState.Rejected) asset.Reject();
                if (item.State is MediaAssetState.PendingDeletion or MediaAssetState.Deleted)
                    asset.RequestDeletion(Now.AddSeconds(2));
                if (item.State == MediaAssetState.Deleted) asset.ConfirmProviderDeletion(Now.AddSeconds(3));
                db.MediaAssets.Add(asset);
                if (index < MemoryInputLimits.HardMaxMediaPerMemory) memory.AttachMedia(item.AssetId, Guid.NewGuid());
            }
            db.Memories.Add(memory);
            await db.SaveChangesAsync();
        }

        public async Task SeedUnlinkedReadyAsset(Guid assetId)
        {
            await using var db = Db();
            var asset = MediaAsset.CreateGuestAsset(assetId, Invitation, MediaKind.Image, Now);
            asset.BeginProcessing($"object-{assetId:N}");
            asset.MarkReady(new NormalizedImageVerificationEvidence($"object-{assetId:N}", "image/webp", 128), Now.AddSeconds(1));
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync();
        }
    }

    private sealed class FakeMemoryDeliveryGateway : IPrivateMediaDeliveryGateway
    {
        public bool Failure { get; set; }
        public Action? OnIssue { get; set; }
        public int ImageCalls { get; private set; }
        public int VideoCalls { get; private set; }

        public Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateImageCapabilityAsync(Guid assetId, DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
        {
            ImageCalls++;
            if (Failure) throw new HttpRequestException("upstream failure");
            OnIssue?.Invoke();
            return Task.FromResult<(Uri Url, DateTimeOffset ExpiresAt)?>((new Uri("https://media.example.test/short-lived"), expiresAt.AddSeconds(-1)));
        }

        public Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateVideoSessionAsync(Guid assetId, DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
        {
            VideoCalls++;
            if (Failure) throw new HttpRequestException("upstream failure");
            OnIssue?.Invoke();
            return Task.FromResult<(Uri Url, DateTimeOffset ExpiresAt)?>((new Uri("https://media.example.test/short-lived"), expiresAt.AddSeconds(-1)));
        }
    }

    private sealed class FakeMediaMaintenance : IMediaProviderAssetMaintenance
    {
        public bool Failure { get; set; }
        public int DeleteCalls { get; private set; }
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            if (Failure) throw new HttpRequestException("simulated temporary delete failure");
            return Task.FromResult(MediaProviderDeletionResult.Deleted);
        }
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(MediaProviderAssetPresence.Present);
    }

    private sealed class FakeGuestMediaUploadAvailability(bool isAvailable) : IGuestMediaUploadAvailability
    {
        public bool IsAvailable { get; set; } = isAvailable;
    }
}
