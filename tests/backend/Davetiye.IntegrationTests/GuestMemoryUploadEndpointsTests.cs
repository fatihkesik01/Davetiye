using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Administration;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Modules.Memories;
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

/// <summary>
/// P6-M3: anonymous guest memory upload capability (create, intent, status, finalize, expiry sweep, purge) over real
/// PostgreSQL with a fake provider behind the Phase 4 gateway ports. No real provider or credential is used.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class GuestMemoryUploadEndpointsTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "TestPassw0rd1";
    private const string Origin = "https://allowed.example.test";
    private const string CookieName = "__Host-davetiye-memory-upload";
    private const long Mb = 1024 * 1024;
    private const string Content = """{"eventType":"dugun","headline":"Memories headline","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Venue","address":"Address"},"message":"Welcome","hostNames":["Ada"],"programItems":[{"title":"Reception","startsAt":"2026-10-10T13:00:00Z"}]}""";

    // ---------------------------------------------------------------- happy path and projection

    [Fact]
    public async Task Guest_creates_pending_memory_uploads_images_and_finalizes_without_any_persistent_token()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "Harika bir gece", displayName: "Ada");

        // Capability is a short-lived HttpOnly Secure SameSite=Strict cookie, never JSON, and only its digest is stored.
        Assert.StartsWith(CookieName + "=", g.SetCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", g.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", g.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", g.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(g.Token, g.Body, StringComparison.Ordinal);
        Assert.Equal(new[] { "createdAt", "limits", "memoryId", "uploadExpiresAt" },
            g.Json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var limits = g.Json.RootElement.GetProperty("limits");
        Assert.Equal(3, limits.GetProperty("maxMediaItems").GetInt32());
        Assert.Equal(10 * Mb, limits.GetProperty("maxImageBytes").GetInt64());
        Assert.Equal(100 * Mb, limits.GetProperty("maxVideoBytes").GetInt64());
        Assert.Equal(60, limits.GetProperty("maxVideoDurationSeconds").GetInt64());

        await using (var db = h.Db())
        {
            var memory = await db.Memories.SingleAsync();
            Assert.Equal(MemoryState.PendingMedia, memory.State);
            var capability = await db.MemoryUploadCapabilities.SingleAsync();
            Assert.Equal("memory-upload", capability.Purpose);
            Assert.Equal(32, capability.HmacDigest.Length);
            Assert.Equal(Now.AddMinutes(10), capability.ExpiresAt);
            Assert.NotEqual(g.Token, Convert.ToBase64String(capability.HmacDigest));
        }

        using var intent = await g.Intent("Image", 2 * Mb);
        Assert.Equal(HttpStatusCode.Created, intent.StatusCode);
        AssertPrivateHeaders(intent);
        using var intentJson = JsonDocument.Parse(await intent.Content.ReadAsStringAsync());
        var assetId = intentJson.RootElement.GetProperty("assetId").GetGuid();
        Assert.False(intentJson.RootElement.GetProperty("replayed").GetBoolean());
        Assert.StartsWith("https://", intentJson.RootElement.GetProperty("ingressUri").GetString(), StringComparison.Ordinal);

        // The provider capability is bound to ONE guest asset, the entitlement ceiling and the memory capability expiry.
        var request = Assert.Single(h.Provider.Requests);
        Assert.Equal(assetId, request.AssetId);
        Assert.Equal(MediaQuotaScope.Guest, request.QuotaScope);
        Assert.Equal(MediaKind.Image, request.Kind);
        Assert.Equal(10 * Mb, request.MaximumBytes);
        Assert.Equal(Now.AddMinutes(10), request.ExpiresAt);
        Assert.Equal("image", h.Provider.Routes.Single());

        await using (var db = h.Db())
        {
            var asset = await db.MediaAssets.SingleAsync();
            Assert.Equal(MediaQuotaScope.Guest, asset.QuotaScope);
            Assert.Equal(h.Invitation, asset.InvitationId);
            Assert.Equal(MediaAssetState.PendingUpload, asset.State);
            Assert.Equal(assetId, (await db.MemoryMedia.SingleAsync()).MediaAssetId);
        }

        // Status needs the capability and reports processing only while the memory is pending.
        using (var pending = await g.Status())
        {
            Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
            AssertPrivateHeaders(pending);
            using var json = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
            Assert.Equal("pendingMedia", json.RootElement.GetProperty("state").GetString());
            var media = Assert.Single(json.RootElement.GetProperty("media").EnumerateArray());
            Assert.Equal("processing", media.GetProperty("status").GetString());
            Assert.Equal("Image", media.GetProperty("kind").GetString());
            Assert.Equal(new[] { "assetId", "kind", "status" }, media.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }

        // Not processed yet: finalize must not publish and must not consume the capability.
        using (var early = await g.Finalize())
        {
            Assert.Equal(HttpStatusCode.Accepted, early.StatusCode);
            Assert.Contains("processing", await early.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await using (var db = h.Db())
        {
            Assert.Equal(MemoryState.PendingMedia, (await db.Memories.SingleAsync()).State);
            Assert.Null((await db.MemoryUploadCapabilities.SingleAsync()).ConsumedAt);
        }

        h.Provider.ImageReady(assetId, 1_500_000);
        using (var done = await g.Finalize())
        {
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
            using var json = JsonDocument.Parse(await done.Content.ReadAsStringAsync());
            Assert.Equal("published", json.RootElement.GetProperty("state").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("acceptedMediaCount").GetInt32());
        }

        await using (var db = h.Db())
        {
            var memory = await db.Memories.Include(m => m.Media).SingleAsync();
            Assert.Equal(MemoryState.Published, memory.State);
            Assert.Single(memory.Media);
            Assert.NotNull((await db.MemoryUploadCapabilities.SingleAsync()).ConsumedAt);
            var asset = await db.MediaAssets.SingleAsync();
            Assert.Equal(MediaAssetState.Ready, asset.State);
            Assert.Equal("image/webp", asset.DetectedContentType);
            // Guest assets are never placed on the invitation (a Creator-only concept).
            Assert.Empty(await db.MediaPlacements.ToListAsync());
        }

        // The capability is consumed: no status, no further intent, no second finalize, no guest edit/delete surface.
        Assert.Equal(HttpStatusCode.NotFound, (await g.Status()).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Finalize()).StatusCode);
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var message = new HttpRequestMessage(method, $"/api/v1/public/invitations/{h.Code}/memories/{g.MemoryId}");
            using var response = await g.Client.SendAsync(message);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        }
    }

    [Fact]
    public async Task Public_list_projects_finalized_memory_media_metadata_without_provider_details()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "photo memory");
        using var intent = await g.Intent("Image", Mb);
        var assetId = (await ReadJson(intent)).RootElement.GetProperty("assetId").GetGuid();
        h.Provider.ImageReady(assetId, 500_000);
        Assert.Equal(HttpStatusCode.OK, (await g.Finalize()).StatusCode);

        using var anonymous = h.Guest();
        using var list = await anonymous.GetAsync($"/api/v1/public/invitations/{h.Code}/memories");
        var body = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "createdAt", "displayName", "emoji", "id", "media", "text" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var media = Assert.Single(item.GetProperty("media").EnumerateArray());
        Assert.Equal(assetId, media.GetProperty("assetId").GetGuid());
        Assert.Equal("Image", media.GetProperty("kind").GetString());
        Assert.DoesNotContain("guest/", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_media_provider_hides_upload_limits_and_rejects_creation_and_intent_without_quota_side_effects()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        h.MediaAvailability.IsAvailable = false;
        using var guest = h.Guest();

        using (var config = await guest.GetAsync($"/api/v1/public/invitations/{h.Code}/memories/configuration"))
        {
            Assert.Equal(HttpStatusCode.OK, config.StatusCode);
            using var json = JsonDocument.Parse(await config.Content.ReadAsStringAsync());
            var upload = json.RootElement.GetProperty("uploadLimits");
            Assert.False(upload.GetProperty("enabled").GetBoolean());
            foreach (var limit in new[] { "maxImages", "maxVideos", "maxImageSizeMb", "maxVideoSizeMb", "maxVideoDurationSeconds" })
                Assert.Equal(0, upload.GetProperty(limit).GetInt64());
        }

        using (var create = await PostJson(guest, $"/api/v1/public/invitations/{h.Code}/memories/with-media",
                   new { text = "not persisted while provider is off" }, await Csrf(guest)))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        await using (var db = h.Db())
        {
            Assert.Empty(await db.Memories.ToListAsync());
            Assert.Empty(await db.MemoryUploadCapabilities.ToListAsync());
        }

        h.MediaAvailability.IsAvailable = true;
        var pending = await h.NewGuestAsync(text: "created before provider was disabled");
        h.MediaAvailability.IsAvailable = false;
        using (var intent = await pending.Intent("Image", Mb))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, intent.StatusCode);
        await using (var db = h.Db())
        {
            Assert.Equal(1, await db.Memories.CountAsync(memory => memory.State == MemoryState.PendingMedia));
            Assert.Single(await db.MemoryUploadCapabilities.ToListAsync());
            Assert.Empty(await db.MediaAssets.ToListAsync());
            Assert.Empty(await db.PendingUploads.ToListAsync());
            Assert.Empty(await db.MemoryMedia.ToListAsync());
        }
        Assert.Empty(h.Provider.Requests);
    }

    // ---------------------------------------------------------------- gates

    [Theory]
    [InlineData("draft")]
    [InlineData("scheduled")]
    [InlineData("paused")]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("entitlement-off")]
    [InlineData("no-guest-media")]
    public async Task Failing_gate_makes_guest_memory_creation_uniformly_404_and_never_creates_rows(string gate)
    {
        await using var h = await CreateAsync();
        if (gate != "draft") await h.Publish(h.Account, h.Invitation, scheduled: gate == "scheduled", paid: gate != "entitlement-off");
        await h.SeedConfiguration(h.Invitation, enabled: gate != "disabled");
        if (gate == "paused") await h.Command("pause");
        if (gate == "expired") h.Clock.Current = Now.AddDays(30);
        if (gate == "no-guest-media") await h.SetGuestLimits(images: 0, videos: 0);

        using var client = h.Guest();
        using var response = await PostJson(client, $"/api/v1/public/invitations/{h.Code}/memories/with-media",
            new { text = "hello" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = h.Db();
        Assert.Empty(await db.Memories.ToListAsync());
        Assert.Empty(await db.MemoryUploadCapabilities.ToListAsync());
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("disabled")]
    [InlineData("window-over")]
    public async Task Gates_closing_after_creation_make_intent_status_and_finalize_uniformly_404(string gate)
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        if (gate == "paused") await h.Command("pause");
        if (gate == "disabled") await h.SetConfiguration(h.Invitation, enabled: false);
        if (gate == "window-over") h.Clock.Current = Now.AddDays(30);

        Assert.Equal(HttpStatusCode.NotFound, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Status()).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Finalize()).StatusCode);
        await using var db = h.Db();
        Assert.Empty(await db.MediaAssets.ToListAsync());
    }

    [Fact]
    public async Task Unknown_or_malformed_public_code_is_404_for_every_guest_upload_route()
    {
        await using var h = await CreateAsync();
        using var client = h.Guest();
        var csrf = await Csrf(client);
        var memory = Guid.NewGuid();
        foreach (var code in new[] { "nope", new string((char)97, 64), "a.b" })
        {
            var root = $"/api/v1/public/invitations/{code}/memories";
            Assert.Equal(HttpStatusCode.NotFound, (await PostJson(client, root + "/with-media", new { text = "x" }, csrf)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostJson(client, $"{root}/{memory}/media/intents",
                new { kind = "Image", declaredByteLength = 5 }, csrf, key: Guid.NewGuid(), cookie: RandomToken())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostJson(client, $"{root}/{memory}/finalize", null, csrf, cookie: RandomToken())).StatusCode);
            using var status = new HttpRequestMessage(HttpMethod.Get, $"{root}/{memory}/status");
            status.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={RandomToken()}");
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(status)).StatusCode);
        }
    }

    [Fact]
    public async Task Creation_requires_antiforgery_and_a_matching_origin()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        using var client = h.Guest();
        var path = $"/api/v1/public/invitations/{h.Code}/memories/with-media";
        Assert.Equal(HttpStatusCode.BadRequest, (await PostJson(client, path, new { text = "x" }, csrf: null)).StatusCode);
        using var wrongOrigin = await PostJson(client, path, new { text = "x" }, await Csrf(client), origin: "https://evil.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        await using var db = h.Db();
        Assert.Empty(await db.Memories.ToListAsync());
    }

    // ---------------------------------------------------------------- replay / cross-capability matrix

    [Fact]
    public async Task Every_foreign_expired_consumed_or_unrelated_capability_gets_the_same_uniform_404()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var other = await h.CreateSecondInvitationAsync();
        var a = await h.NewGuestAsync(text: "memory A");
        var b = await h.NewGuestAsync(text: "memory B");
        var c = await h.NewGuestAsync(text: "memory on another invitation", other);

        using var aIntent = await a.Intent("Image", Mb);
        var aAsset = (await ReadJson(aIntent)).RootElement.GetProperty("assetId").GetGuid();
        var ingressToken = (await ReadJson(aIntent)).RootElement.GetProperty("ingressHeaders").GetProperty("X-Media-Capability").GetString()!;
        var intentBody = new { kind = "Image", declaredByteLength = Mb };

        var probes = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>();
        // Other memory, same invitation.
        probes.Add(("memory A token on memory B", () => h.Probe(h.Code, b.MemoryId, "intent", a.Token, intentBody)));
        probes.Add(("memory A token on memory B status", () => h.Probe(h.Code, b.MemoryId, "status", a.Token)));
        probes.Add(("memory A token on memory B finalize", () => h.Probe(h.Code, b.MemoryId, "finalize", a.Token)));
        // Other invitation: valid memory of invitation 2 reached with invitation 1's capability and vice versa.
        probes.Add(("memory A token on invitation 2 route", () => h.Probe(other.Code, a.MemoryId, "intent", a.Token, intentBody)));
        probes.Add(("memory C token on invitation 1 route", () => h.Probe(h.Code, c.MemoryId, "intent", c.Token, intentBody)));
        probes.Add(("memory C token on invitation 2 route, memory A", () => h.Probe(other.Code, a.MemoryId, "status", c.Token)));
        // Provider capability, Creator-style signed token, RSVP-style random token, wrong-shaped and missing cookies.
        probes.Add(("provider ingress capability as cookie", () => h.Probe(h.Code, a.MemoryId, "intent", ingressToken, intentBody)));
        probes.Add(("rsvp style token", () => h.Probe(h.Code, a.MemoryId, "intent", RandomToken(), intentBody)));
        probes.Add(("wrong shape", () => h.Probe(h.Code, a.MemoryId, "status", "short")));
        probes.Add(("no cookie", () => h.Probe(h.Code, a.MemoryId, "status", null)));
        probes.Add(("random memory id", () => h.Probe(h.Code, Guid.NewGuid(), "status", a.Token)));
        probes.Add(("empty memory id", () => h.Probe(h.Code, Guid.Empty, "finalize", a.Token)));

        var baseline = new List<(string, HttpStatusCode, string)>();
        foreach (var (name, send) in probes)
        {
            using var response = await send();
            baseline.Add((name, response.StatusCode, System.Text.RegularExpressions.Regex.Replace(
                await response.Content.ReadAsStringAsync(), "\"(traceId|correlationId)\":\"[^\"]*\"", "\"id\":\"-\"")));
        }
        Assert.All(baseline, item => Assert.Equal(HttpStatusCode.NotFound, item.Item2));
        Assert.Single(baseline.Select(item => item.Item3).Distinct());

        // A logged-in Creator session (and the Creator route's own cookies) grants nothing on the guest routes.
        using (var creator = await h.LoginAsync(h.Email))
        {
            using var status = await creator.GetAsync($"/api/v1/public/invitations/{h.Code}/memories/{a.MemoryId}/status");
            Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        }

        // Nothing leaked: no extra assets, no state change.
        await using (var db = h.Db())
        {
            Assert.Equal(1, await db.MediaAssets.CountAsync());
            Assert.All(await db.Memories.ToListAsync(), memory => Assert.Equal(MemoryState.PendingMedia, memory.State));
        }

        // Expired capability.
        h.Clock.Current = Now.AddMinutes(11);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Status()).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Finalize()).StatusCode);
        using var late = await h.Probe(h.Code, a.MemoryId, "status", a.Token);
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
        Assert.Equal(aAsset, (await h.Db().MediaAssets.SingleAsync()).Id);
    }

    [Fact]
    public async Task Consumed_capability_cannot_be_replayed_by_a_stolen_copy_of_the_cookie()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "text only memory");
        Assert.Equal(HttpStatusCode.OK, (await g.Finalize()).StatusCode);
        foreach (var kind in new[] { "status", "intent", "finalize" })
        {
            using var replay = await h.Probe(h.Code, g.MemoryId, kind, g.Token, new { kind = "Image", declaredByteLength = Mb });
            Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        }
    }

    // ---------------------------------------------------------------- limits and quota

    [Fact]
    public async Task Oversize_wrong_kind_or_duration_requests_are_rejected_before_any_provider_call_or_reservation()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");

        var rejected = new (string? Kind, long Bytes, long? Duration)[]
        {
            ("Image", 10 * Mb + 1, null),          // over the image ceiling
            ("Video", 100 * Mb + 1, 30),           // over the video ceiling
            ("Video", 50 * Mb, 61),                // over the duration ceiling
            ("Video", 50 * Mb, null),              // video needs a duration
            ("Video", 50 * Mb, 0),
            ("Image", 5 * Mb, 5),                  // duration is video-only
            ("Image", 0, null),
            ("Image", -5, null),
            ("Gif", Mb, null),                     // wrong kind
            ("image", Mb, null),                   // kinds are case-sensitive
            (null, Mb, null),
        };
        foreach (var item in rejected)
        {
            using var response = await g.IntentRaw(new { kind = item.Kind, declaredByteLength = item.Bytes, declaredDurationSeconds = item.Duration });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var noKey = await g.Intent("Image", Mb, key: Guid.Empty, sendKey: false))
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);

        Assert.Empty(h.Provider.Requests);
        await using var db = h.Db();
        Assert.Empty(await db.MediaAssets.ToListAsync());
        Assert.Empty(await db.PendingUploads.ToListAsync());
        Assert.Empty(await db.MemoryMedia.ToListAsync());
    }

    [Fact]
    public async Task Per_memory_media_count_is_enforced_server_side()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Created, (await g.Intent("Image", Mb)).StatusCode);
        using var fourth = await g.Intent("Image", Mb);
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Contains("memory_media_limit", await fourth.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(3, h.Provider.Requests.Count);
        await using var db = h.Db();
        Assert.Equal(3, await db.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task Guest_quota_is_separate_from_creator_quota_and_counts_pending_assets_as_used()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        await h.SetGuestLimits(images: 2, videos: 1);
        // A Creator-scope asset must not consume the Guest quota (PD-06).
        await h.SeedCreatorAsset();
        var g = await h.NewGuestAsync(text: "x");
        var second = await h.NewGuestAsync(text: "y");
        Assert.Equal(HttpStatusCode.Created, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.Intent("Image", Mb)).StatusCode);
        using var over = await g.Intent("Image", Mb);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Contains("media_quota_reached", await over.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // The video quota is its own counter.
        Assert.Equal(HttpStatusCode.Created, (await g.Intent("Video", 20 * Mb, duration: 30)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await second.Intent("Video", 20 * Mb, duration: 30)).StatusCode);
        await using var db = h.Db();
        Assert.Equal(3, await db.MediaAssets.CountAsync(a => a.QuotaScope == MediaQuotaScope.Guest));
    }

    [Fact]
    public async Task Concurrent_intents_cannot_exceed_the_invitation_guest_quota()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        await h.SetGuestLimits(images: 3, videos: 1);
        var guests = new List<GuestSession>();
        for (var i = 0; i < 8; i++) guests.Add(await h.NewGuestAsync(text: $"m{i}"));

        var statuses = await Task.WhenAll(guests.Select(async guest =>
        {
            using var response = await guest.Intent("Image", Mb);
            return response.StatusCode;
        }));

        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Conflict));
        await using var db = h.Db();
        Assert.Equal(3, await db.MediaAssets.CountAsync(a => a.QuotaScope == MediaQuotaScope.Guest));
        Assert.Equal(3, await db.PendingUploads.CountAsync());
        Assert.Equal(3, h.Provider.Requests.Count);
    }

    [Fact]
    public async Task Concurrent_intents_on_one_memory_cannot_exceed_the_per_memory_count()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var response = await g.Intent("Image", Mb);
            return response.StatusCode;
        }));
        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Conflict));
        await using var db = h.Db();
        Assert.Equal(3, await db.MemoryMedia.CountAsync());
    }

    // ---------------------------------------------------------------- idempotency and provider failure

    [Fact]
    public async Task Same_idempotency_key_replays_the_same_asset_and_never_consumes_more_quota_or_count()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var key = Guid.NewGuid();
        using var first = await g.Intent("Image", 2 * Mb, key: key);
        using var second = await g.Intent("Image", 2 * Mb, key: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstAsset = (await ReadJson(first)).RootElement.GetProperty("assetId").GetGuid();
        var secondJson = await ReadJson(second);
        Assert.Equal(firstAsset, secondJson.RootElement.GetProperty("assetId").GetGuid());
        Assert.True(secondJson.RootElement.GetProperty("replayed").GetBoolean());
        await using (var db = h.Db())
        {
            Assert.Equal(1, await db.MediaAssets.CountAsync());
            Assert.Equal(1, await db.MemoryMedia.CountAsync());
        }

        // Same key with a different payload, or on another memory, is a conflict and creates nothing.
        using var changed = await g.Intent("Image", 3 * Mb, key: key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var other = await h.NewGuestAsync(text: "other");
        using var foreignKey = await other.Intent("Image", 2 * Mb, key: key);
        Assert.Equal(HttpStatusCode.Conflict, foreignKey.StatusCode);
        Assert.Contains("idempotency_conflict", await foreignKey.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var after = h.Db();
        Assert.Equal(1, await after.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task Concurrent_guest_reservations_reusing_a_key_across_invitations_return_one_success_and_one_conflict()
    {
        await using var h = await CreateAsync(synchronizeIdempotencyLookups: true);
        await h.ReadyInvitation();
        var other = await h.CreateSecondInvitationAsync();
        var firstGuest = await h.NewGuestAsync(text: "first invitation");
        var secondGuest = await h.NewGuestAsync(text: "second invitation", target: other);
        var key = Guid.NewGuid();
        h.IdempotencyBarrier.Arm(key);

        var attempts = await Task.WhenAll(
            firstGuest.Intent("Image", 2 * Mb, key: key),
            secondGuest.Intent("Image", 2 * Mb, key: key));
        using var first = attempts[0];
        using var second = attempts[1];

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("idempotency_conflict", conflictBody, StringComparison.Ordinal);
        Assert.DoesNotContain(firstGuest.MemoryId.ToString(), conflictBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secondGuest.MemoryId.ToString(), conflictBody, StringComparison.OrdinalIgnoreCase);
        await using var verify = h.Db();
        Assert.Equal(1, await verify.PendingUploads.CountAsync(upload => upload.IdempotencyKey == key));
        Assert.Equal(1, await verify.MediaAssets.CountAsync(asset => asset.QuotaScope == MediaQuotaScope.Guest));
        Assert.Equal(1, await verify.MemoryMedia.CountAsync());
    }

    [Fact]
    public async Task Guest_idempotency_key_collision_with_creator_upload_returns_safe_conflict()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var guest = await h.NewGuestAsync(text: "guest upload");
        var key = Guid.NewGuid();
        var creatorAssetId = Guid.NewGuid();
        await using (var db = h.Db())
        {
            var creatorAsset = MediaAsset.CreateCreatorAsset(creatorAssetId, h.Invitation, MediaKind.Image, Now);
            var creatorIntent = PendingUpload.Create(Guid.NewGuid(), creatorAssetId, key, MediaPresentationRole.Cover,
                Mb, new string('a', 64), Now, Now.AddMinutes(10), 10 * Mb, 0);
            db.MediaAssets.Add(creatorAsset);
            db.PendingUploads.Add(creatorIntent);
            await db.SaveChangesAsync();
        }

        using var response = await guest.Intent("Image", 2 * Mb, key: key);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("idempotency_conflict", body, StringComparison.Ordinal);
        Assert.DoesNotContain(creatorAssetId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        await using var verify = h.Db();
        Assert.Equal(1, await verify.PendingUploads.CountAsync(upload => upload.IdempotencyKey == key));
        Assert.Equal(MediaQuotaScope.Creator,
            await verify.MediaAssets.Where(asset => asset.Id == creatorAssetId).Select(asset => asset.QuotaScope).SingleAsync());
    }

    [Fact]
    public async Task Provider_failure_returns_503_keeps_the_reservation_and_a_retry_with_the_same_key_succeeds()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var key = Guid.NewGuid();
        h.Provider.Fail = true;
        using var failed = await g.Intent("Video", 20 * Mb, key: key, duration: 30);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        h.Provider.Fail = false;
        using var retry = await g.Intent("Video", 20 * Mb, key: key, duration: 30);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("video", h.Provider.Routes.Last());
        var request = h.Provider.Requests.Last();
        Assert.Equal(MediaQuotaScope.Guest, request.QuotaScope);
        Assert.Equal(100 * Mb, request.MaximumBytes);
        Assert.Equal(60, request.MaximumDurationSeconds);
        await using var db = h.Db();
        Assert.Equal(1, await db.MediaAssets.CountAsync());
    }

    // ---------------------------------------------------------------- finalize transitions

    [Fact]
    public async Task Finalize_with_all_media_ready_publishes_the_memory_with_every_asset()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: null);
        var image = await g.IntentAsset("Image", 2 * Mb);
        var video = await g.IntentAsset("Video", 30 * Mb, duration: 20);
        h.Provider.ImageReady(image, Mb);
        h.Provider.VideoReady(video, 25 * Mb, 20);
        using var done = await g.Finalize();
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var json = await ReadJson(done);
        Assert.Equal("published", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("acceptedMediaCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("rejectedMediaCount").GetInt32());
        await using var db = h.Db();
        var memory = await db.Memories.Include(m => m.Media).SingleAsync();
        Assert.Equal(MemoryState.Published, memory.State);
        Assert.Equal(2, memory.Media.Count);
        Assert.All(await db.MediaAssets.ToListAsync(), asset => Assert.Equal(MediaAssetState.Ready, asset.State));
    }

    [Fact]
    public async Task Finalize_drops_rejected_assets_and_publishes_when_at_least_one_is_ready()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: null);
        var good = await g.IntentAsset("Image", 2 * Mb);
        var bad = await g.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageReady(good, Mb);
        h.Provider.ImageReady(bad, 3 * Mb); // actual bytes exceed the declared size -> rejected by server verification
        using var done = await g.Finalize();
        var json = await ReadJson(done);
        Assert.Equal("published", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("acceptedMediaCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("rejectedMediaCount").GetInt32());
        await using var db = h.Db();
        var memory = await db.Memories.Include(m => m.Media).SingleAsync();
        Assert.Equal(MemoryState.Published, memory.State);
        Assert.Equal(good, Assert.Single(memory.Media).MediaAssetId);
        Assert.Equal(MediaAssetState.Rejected, (await db.MediaAssets.SingleAsync(a => a.Id == bad)).State);
        // The rejected asset keeps consuming Guest quota until provider deletion is confirmed (PD-16).
        Assert.Equal(2, await db.MediaAssets.CountAsync(a => a.QuotaScope == MediaQuotaScope.Guest));
    }

    [Fact]
    public async Task Finalize_with_every_asset_rejected_and_no_text_discards_the_memory_and_revokes_the_capability()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: null);
        var bad = await g.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageReady(bad, 5 * Mb);
        using var done = await g.Finalize();
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var json = await ReadJson(done);
        Assert.Equal("rejected", json.RootElement.GetProperty("state").GetString());
        await using var db = h.Db();
        Assert.Equal(MemoryState.Abandoned, (await db.Memories.SingleAsync()).State);
        Assert.NotNull((await db.MemoryUploadCapabilities.SingleAsync()).RevokedAt);
        Assert.Equal(MediaAssetState.Rejected, (await db.MediaAssets.SingleAsync()).State);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Status()).StatusCode);
    }

    [Fact]
    public async Task Finalize_with_every_asset_rejected_keeps_a_text_memory_as_text_only()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "I still wrote this");
        var bad = await g.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageEvidence[bad] = new NormalizedImageVerificationEvidence($"guest/{bad:N}.webp", "image/jpeg", Mb);
        using var done = await g.Finalize();
        var json = await ReadJson(done);
        Assert.Equal("published", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("acceptedMediaCount").GetInt32());
        await using var db = h.Db();
        var memory = await db.Memories.Include(m => m.Media).SingleAsync();
        Assert.Equal(MemoryState.Published, memory.State);
        Assert.Empty(memory.Media);
        Assert.Equal(MediaAssetState.Rejected, (await db.MediaAssets.SingleAsync()).State);
    }

    [Fact]
    public async Task Finalize_stays_processing_until_every_asset_is_terminal_then_publishes_on_retry()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var first = await g.IntentAsset("Image", 2 * Mb);
        var second = await g.IntentAsset("Video", 30 * Mb, duration: 20);
        h.Provider.ImageReady(first, Mb);
        using (var partial = await g.Finalize()) Assert.Equal(HttpStatusCode.Accepted, partial.StatusCode);
        using (var status = await g.Status())
        {
            var json = await ReadJson(status);
            var statuses = json.RootElement.GetProperty("media").EnumerateArray().Select(m => m.GetProperty("status").GetString()).Order().ToArray();
            Assert.Equal(new[] { "processing", "ready" }, statuses);
        }
        // Video still encoding at the provider.
        h.Provider.VideoInspections[second] = new MediaProviderVideoInspection("uid-" + second.ToString("N"), "inprogress", false, null, null, null, null);
        using (var stillPending = await g.Finalize()) Assert.Equal(HttpStatusCode.Accepted, stillPending.StatusCode);
        h.Provider.VideoReady(second, 25 * Mb, 20);
        using var done = await g.Finalize();
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("published", (await ReadJson(done)).RootElement.GetProperty("state").GetString());
    }


    [Fact]
    public async Task Provider_inspection_outage_during_finalize_keeps_the_memory_pending_instead_of_failing()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var image = await g.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageReady(image, Mb);
        h.Provider.VerifyThrows = true;
        using (var outage = await g.Finalize()) Assert.Equal(HttpStatusCode.Accepted, outage.StatusCode);
        h.Provider.VerifyThrows = false;
        using var done = await g.Finalize();
        Assert.Equal("published", (await ReadJson(done)).RootElement.GetProperty("state").GetString());
    }
    [Fact]
    public async Task Provider_evidence_not_client_claims_decides_ready_and_video_is_not_claimed_to_be_stripped()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var image = await g.IntentAsset("Image", 2 * Mb);
        var video = await g.IntentAsset("Video", 30 * Mb, duration: 20);
        // Images go through the normalizing (EXIF/GPS-stripping) Worker ingress, videos through the Stream gateway.
        Assert.Equal(new[] { "image", "video" }, h.Provider.Routes.ToArray());
        // Unnormalized evidence (JPEG, or no evidence at all) can never make an image Ready.
        h.Provider.ImageEvidence[image] = new NormalizedImageVerificationEvidence($"guest/{image:N}.jpg", "image/jpeg", Mb);
        h.Provider.VideoReady(video, 25 * Mb, 70); // longer than the 60 second ceiling -> rejected
        using var done = await g.Finalize();
        Assert.Equal("published", (await ReadJson(done)).RootElement.GetProperty("state").GetString());
        await using var db = h.Db();
        Assert.All(await db.MediaAssets.ToListAsync(), asset => Assert.Equal(MediaAssetState.Rejected, asset.State));
    }

    // ---------------------------------------------------------------- expiry sweep

    [Fact]
    public async Task Expired_pending_memory_is_abandoned_by_the_media_lifecycle_job_and_its_quota_is_retained()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        await h.SetGuestLimits(images: 2, videos: 1);
        var abandoned = await h.NewGuestAsync(text: "stale");
        var ready = await abandoned.IntentAsset("Image", 2 * Mb);
        var pending = await abandoned.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageReady(ready, Mb);
        // Finalize verifies the ready asset but the other one is still pending, so the memory stays PendingMedia.
        using (var partial = await abandoned.Finalize()) Assert.Equal(HttpStatusCode.Accepted, partial.StatusCode);
        var fresh = await h.NewGuestAsync(text: "fresh-but-no-media");

        h.Clock.Current = Now.AddMinutes(11);
        var jobs = h.Services.CreateAsyncScope();
        await using (jobs)
        {
            var result = await jobs.ServiceProvider.GetRequiredService<IMediaLifecycleJobs>().RunBatchAsync(100, default);
            Assert.Equal(2, result.AbandonedGuestMemories); // both pending memories lost their capability
        }

        await using var db = h.Db();
        var memories = await db.Memories.ToListAsync();
        Assert.All(memories, memory => Assert.Equal(MemoryState.Abandoned, memory.State));
        Assert.All(await db.MemoryUploadCapabilities.ToListAsync(), capability => Assert.NotNull(capability.RevokedAt));
        Assert.Equal(MediaAssetState.PendingDeletion, (await db.MediaAssets.SingleAsync(a => a.Id == ready)).State);
        Assert.Equal(MediaAssetState.Rejected, (await db.MediaAssets.SingleAsync(a => a.Id == pending)).State);
        Assert.All(await db.PendingUploads.ToListAsync(), intent => Assert.True(intent.ConsumedAt is not null || intent.CancelledAt is not null));
        // Bytes are kept until purge: no provider delete is attempted and the quota is still consumed.
        Assert.Empty(h.Provider.Deleted);
        Assert.Equal(2, await db.MediaAssets.CountAsync(a => a.QuotaScope == MediaQuotaScope.Guest));

        // Abandoned capabilities are dead everywhere and the guest quota is still full.
        Assert.Equal(HttpStatusCode.NotFound, (await abandoned.Status()).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fresh.Intent("Image", Mb)).StatusCode);
        var again = await h.NewGuestAsync(text: "after");
        using var quota = await again.Intent("Image", Mb);
        Assert.Equal(HttpStatusCode.Conflict, quota.StatusCode);
        Assert.Contains("media_quota_reached", await quota.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abandoned_empty_sessions_are_cleaned_in_bounded_batches_after_configured_retention_without_removing_media_quota_rows()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        await h.SetGuestLimits(images: 1, videos: 1);

        var emptySessions = new List<GuestSession>();
        for (var index = 0; index < 5; index++) emptySessions.Add(await h.NewGuestAsync(text: null));
        var withIntent = await h.NewGuestAsync(text: null);
        var assetId = await withIntent.IntentAsset("Image", Mb);

        h.Clock.Current = Now.AddMinutes(11);
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var sweeper = scope.ServiceProvider.GetRequiredService<IMemoryUploadExpirySweeper>();
            Assert.Equal(6, await sweeper.SweepAsync(100, default));
        }

        await using (var retained = h.Db())
        {
            Assert.Equal(6, await retained.Memories.CountAsync(memory => memory.State == MemoryState.Abandoned));
            Assert.Equal(6, await retained.MemoryUploadCapabilities.CountAsync());
            Assert.Equal(MediaAssetState.Rejected, (await retained.MediaAssets.SingleAsync(asset => asset.Id == assetId)).State);
            var intent = await retained.PendingUploads.SingleAsync(item => item.MediaAssetId == assetId);
            Assert.NotNull(intent.CancelledAt);
        }

        // Metadata survives until the configured 30-day period from capability revocation has elapsed.
        h.Clock.Current = Now.AddMinutes(11).AddDays(29).AddHours(23);
        await using (var scope = h.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IMemoryUploadExpirySweeper>().SweepAsync(2, default));
        await using (var beforeRetention = h.Db())
        {
            Assert.Equal(6, await beforeRetention.Memories.CountAsync(memory => memory.State == MemoryState.Abandoned));
            Assert.Equal(6, await beforeRetention.MemoryUploadCapabilities.CountAsync());
        }

        h.Clock.Current = Now.AddMinutes(11).AddDays(30).AddSeconds(1);
        for (var expectedRemaining = 4; expectedRemaining >= 0; expectedRemaining -= 2)
        {
            await using var scope = h.Services.CreateAsyncScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IMemoryUploadExpirySweeper>().SweepAsync(2, default));
            await using var db = h.Db();
            Assert.Equal(expectedRemaining, await db.Memories.CountAsync(memory => memory.State == MemoryState.Abandoned));
            Assert.Equal(expectedRemaining, await db.MemoryUploadCapabilities.CountAsync());
        }

        await using (var afterRetention = h.Db())
        {
            Assert.Empty(await afterRetention.Memories.ToListAsync());
            Assert.Empty(await afterRetention.MemoryMedia.ToListAsync());
            Assert.Empty(await afterRetention.MemoryUploadCapabilities.ToListAsync());
            Assert.Equal(MediaAssetState.Rejected, (await afterRetention.MediaAssets.SingleAsync(asset => asset.Id == assetId)).State);
            Assert.NotNull(await afterRetention.PendingUploads.SingleOrDefaultAsync(item => item.MediaAssetId == assetId));
        }
        Assert.Empty(h.Provider.Deleted);
        await using (var quotaScope = h.Services.CreateAsyncScope())
        {
            var usage = await quotaScope.ServiceProvider.GetRequiredService<IGuestMediaStore>()
                .GetGuestUsageAsync(h.Invitation, default);
            Assert.Equal(1, usage.Images);
        }

    }

    [Fact]
    public async Task Sweep_ignores_memories_with_a_live_capability_and_published_memories()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var live = await h.NewGuestAsync(text: "live");
        var finished = await h.NewGuestAsync(text: "done");
        Assert.Equal(HttpStatusCode.OK, (await finished.Finalize()).StatusCode);
        h.Clock.Current = Now.AddMinutes(5);
        await using var scope = h.Services.CreateAsyncScope();
        var swept = await scope.ServiceProvider.GetRequiredService<IMemoryUploadExpirySweeper>().SweepAsync(100, default);
        Assert.Equal(0, swept);
        await using var db = h.Db();
        Assert.Equal(MemoryState.PendingMedia, (await db.Memories.SingleAsync(m => m.Id == live.MemoryId)).State);
        Assert.Equal(MemoryState.Published, (await db.Memories.SingleAsync(m => m.Id == finished.MemoryId)).State);
    }

    // ---------------------------------------------------------------- rate limits

    [Fact]
    public async Task Per_ip_policies_limit_creation_intent_and_finalize()
    {
        await using var h = await CreateAsync(ipCreate: 2, ipIntent: 2, ipFinalize: 1);
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "1");
        await h.NewGuestAsync(text: "2");
        using var client = h.Guest();
        using var third = await PostJson(client, $"/api/v1/public/invitations/{h.Code}/memories/with-media",
            new { text = "3" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await g.Intent("Image", Mb)).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await g.Finalize()).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await g.Finalize()).StatusCode);
    }

    [Fact]
    public async Task Per_invitation_intent_limiter_applies_only_after_the_capability_is_valid()
    {
        await using var h = await CreateAsync(perInvitationIntent: 2);
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var other = await h.NewGuestAsync(text: "y");
        Assert.Equal(HttpStatusCode.Created, (await g.Intent("Image", Mb)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.Intent("Image", Mb)).StatusCode);
        using var limited = await g.Intent("Image", Mb);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        // An invalid capability still gets the uniform 404 even though the invitation limiter is exhausted.
        using var probe = await h.Probe(h.Code, g.MemoryId, "intent", RandomToken(), new { kind = "Image", declaredByteLength = Mb });
        Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
        await using var db = h.Db();
        Assert.Equal(2, await db.MediaAssets.CountAsync());
    }

    // ---------------------------------------------------------------- caps and isolation

    [Fact]
    public async Task Pending_memories_count_toward_the_memory_cap_and_abandoned_ones_do_not()
    {
        await using var h = await CreateAsync(maxMemories: 2);
        await h.ReadyInvitation();
        var first = await h.NewGuestAsync(text: "a");
        await h.NewGuestAsync(text: "b");
        using var client = h.Guest();
        using var capped = await PostJson(client, $"/api/v1/public/invitations/{h.Code}/memories/with-media",
            new { text = "c" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.Conflict, capped.StatusCode);
        Assert.Contains("memory_quota_reached", await capped.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        h.Clock.Current = Now.AddMinutes(11);
        await using (var scope = h.Services.CreateAsyncScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<IMemoryUploadExpirySweeper>().SweepAsync(100, default));
        var third = await h.NewGuestAsync(text: "c");
        Assert.NotEqual(first.MemoryId, third.MemoryId);
    }

    [Fact]
    public async Task Memory_text_validation_applies_to_media_memories_and_stored_xss_stays_inert()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        using var client = h.Guest();
        var csrf = await Csrf(client);
        var path = $"/api/v1/public/invitations/{h.Code}/memories/with-media";
        using (var tooLong = await PostJson(client, path, new { text = new string('x', 501) }, csrf))
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using (var created = await PostJson(client, path, new { text = "<script>alert(1)</script>" }, csrf))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using var db = h.Db();
        Assert.Equal("<script>alert(1)</script>", (await db.Memories.SingleAsync()).Text);
    }

    // ---------------------------------------------------------------- purge

    [Fact]
    public async Task Permanent_purge_removes_guest_memories_assets_and_queues_provider_deletion()
    {
        await using var h = await CreateAsync();
        await h.ReadyInvitation();
        var g = await h.NewGuestAsync(text: "x");
        var ready = await g.IntentAsset("Image", 2 * Mb);
        var pending = await g.IntentAsset("Image", 2 * Mb);
        h.Provider.ImageReady(ready, Mb);
        Assert.Equal(HttpStatusCode.Accepted, (await g.Finalize()).StatusCode);

        await using (var scope = h.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IMemoriesPurgeCoordinator>().PurgeForInvitationAsync(h.Invitation, default);
            var purged = await scope.ServiceProvider.GetRequiredService<IMediaPurgeCoordinator>()
                .EnqueueAndRemoveInvitationAssetsAsync(h.Invitation, default);
            Assert.Equal(2, purged.Assets);
            await transaction.CommitAsync();
        }

        await using var verify = h.Db();
        Assert.Empty(await verify.MediaAssets.ToListAsync());
        Assert.Empty(await verify.PendingUploads.ToListAsync());
        Assert.Empty(await verify.Memories.ToListAsync());
        Assert.Empty(await verify.MemoryMedia.ToListAsync());
        Assert.Empty(await verify.MemoryUploadCapabilities.ToListAsync());
        var queued = await verify.OutboxMessages.Where(m => m.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion).ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(new[] { ready, pending }, id => Assert.Contains(queued, m => m.Payload.Contains(id.ToString("N"), StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertPrivateHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.ETag);
        Assert.Contains("no-referrer", response.Headers.GetValues("Referrer-Policy"));
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string RandomToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed record CsrfResponse(string Token);

    private static async Task<string> Csrf(HttpClient client) =>
        (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token"))!.Token;

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object? body, string? csrf,
        Guid? key = null, string? cookie = null, string origin = Origin)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        message.Headers.TryAddWithoutValidation("Origin", origin);
        if (csrf is not null) message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
        if (key is not null) message.Headers.TryAddWithoutValidation("Idempotency-Key", key.Value.ToString());
        if (cookie is not null) message.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={cookie}");
        return await client.SendAsync(message);
    }

    private sealed class IdempotencyLookupBarrier
    {
        private Guid key;
        private int arrivals;
        private TaskCompletionSource released = NewSignal();

        public void Arm(Guid idempotencyKey)
        {
            key = idempotencyKey;
            arrivals = 0;
            released = NewSignal();
        }

        public async Task AfterLookupAsync(Guid idempotencyKey, CancellationToken cancellationToken)
        {
            if (idempotencyKey != key) return;
            if (Interlocked.Increment(ref arrivals) == 2) released.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BarrierGuestMediaStore(IGuestMediaStore inner, IdempotencyLookupBarrier barrier) : IGuestMediaStore
    {
        public async Task<GuestMediaReplaySnapshot?> FindByIdempotencyKeyAsync(Guid key, CancellationToken cancellationToken)
        {
            var result = await inner.FindByIdempotencyKeyAsync(key, cancellationToken);
            await barrier.AfterLookupAsync(key, cancellationToken);
            return result;
        }

        public Task<(long Images, long Videos)> GetGuestUsageAsync(Guid invitationId, CancellationToken cancellationToken) =>
            inner.GetGuestUsageAsync(invitationId, cancellationToken);

        public Task<bool> SaveReservationAsync(GuestMediaReservationCommand command, Guid assetId, long maximumBytes,
            long maximumDurationSeconds, string requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken) =>
            inner.SaveReservationAsync(command, assetId, maximumBytes, maximumDurationSeconds, requestFingerprint, now,
                cancellationToken);

        public Task DiscardAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
            CancellationToken cancellationToken) => inner.DiscardAsync(invitationId, assetIds, now, cancellationToken);

        public Task DeleteOwnerAssetsAsync(Guid invitationId, IReadOnlyCollection<Guid> assetIds, DateTimeOffset now,
            CancellationToken cancellationToken) => inner.DeleteOwnerAssetsAsync(invitationId, assetIds, now, cancellationToken);
    }

    private async Task<Harness> CreateAsync(int? maxMemories = null, int ipCreate = 500, int ipIntent = 500, int ipFinalize = 500,
        int perInvitationIntent = 1000, bool synchronizeIdempotencyLookups = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false }.ConnectionString;
        var h = new Harness(connection, maxMemories, ipCreate, ipIntent, ipFinalize, perInvitationIntent,
            synchronizeIdempotencyLookups);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new AbandonedMemoryRetentionSettingsInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        await h.AddCreatorAsync(db, h.Account, h.Invitation, h.Code, h.Email);
        await db.SaveChangesAsync();
        return h;
    }

    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }

    /// <summary>Fake provider behind the Phase 4 gateway ports. No network, no credentials.</summary>
    private sealed class FakeProvider : IMediaUploadGateway, IMediaImageNormalizationPipeline
    {
        public List<MediaUploadRequest> Requests { get; } = [];
        public List<string> Routes { get; } = [];
        public List<string> Deleted { get; } = [];
        public Dictionary<Guid, NormalizedImageVerificationEvidence?> ImageEvidence { get; } = [];
        public Dictionary<Guid, MediaProviderVideoInspection?> VideoInspections { get; } = [];
        public bool Fail { get; set; }
        public bool VerifyThrows { get; set; }

        public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(MediaUploadRequest request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("fake provider unavailable");
            Requests.Add(request);
            Routes.Add("image");
            return Task.FromResult(new MediaUploadCapability(new Uri($"https://worker.example.test/v1/images/{request.AssetId:N}"),
                request.ExpiresAt, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Media-Capability"] = "signed-" + request.AssetId.ToString("N") }));
        }

        public Task<MediaUploadCapability> CreateVideoCapabilityAsync(MediaUploadRequest request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("fake provider unavailable");
            Requests.Add(request);
            Routes.Add("video");
            return Task.FromResult(new MediaUploadCapability(new Uri($"https://upload.example.test/tus/{request.AssetId:N}"), request.ExpiresAt));
        }

        public Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken) =>
            VerifyThrows ? throw new HttpRequestException("inspection unavailable") : Task.FromResult(ImageEvidence.GetValueOrDefault(assetId));

        public Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult(VideoInspections.GetValueOrDefault(assetId));

        public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken)
        {
            Deleted.Add(providerObjectReference);
            return Task.CompletedTask;
        }

        public void ImageReady(Guid assetId, long bytes) =>
            ImageEvidence[assetId] = new NormalizedImageVerificationEvidence($"guest/{assetId:N}.webp", "image/webp", bytes);

        public void VideoReady(Guid assetId, long bytes, int seconds)
        {
            var uid = "uid-" + assetId.ToString("N");
            VideoInspections[assetId] = new MediaProviderVideoInspection(uid, "ready", true, bytes, seconds, "video/mp4",
                new MediaVerificationEvidence(uid, "video/mp4", bytes, seconds));
        }
    }

    private sealed class FakeMaintenance : IMediaProviderAssetMaintenance
    {
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken) =>
            Task.FromResult(MediaProviderDeletionResult.AlreadyAbsent);
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(MediaProviderAssetPresence.Present);
    }

    private sealed class FakeGuestMediaUploadAvailability(bool isAvailable) : IGuestMediaUploadAvailability
    {
        public bool IsAvailable { get; set; } = isAvailable;
    }

    private sealed class GuestSession(string code, HttpClient client, string csrf, Guid memoryId,
        string token, string setCookie, string body)
    {
        public HttpClient Client { get; } = client;
        public Guid MemoryId { get; } = memoryId;
        public string Token { get; } = token;
        public string SetCookie { get; } = setCookie;
        public string Body { get; } = body;
        public JsonDocument Json { get; } = JsonDocument.Parse(body);
        private string Root => $"/api/v1/public/invitations/{code}/memories/{MemoryId}";

        public Task<HttpResponseMessage> Intent(string? kind, long bytes, Guid? key = null, long? duration = null, bool sendKey = true) =>
            PostJson(Client, Root + "/media/intents", new { kind, declaredByteLength = bytes, declaredDurationSeconds = duration },
                csrf, sendKey ? key ?? Guid.NewGuid() : null, Token);

        public Task<HttpResponseMessage> IntentRaw(object body) =>
            PostJson(Client, Root + "/media/intents", body, csrf, Guid.NewGuid(), Token);

        public async Task<Guid> IntentAsset(string kind, long bytes, long? duration = null)
        {
            using var response = await Intent(kind, bytes, duration: duration);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await ReadJson(response)).RootElement.GetProperty("assetId").GetGuid();
        }

        public Task<HttpResponseMessage> Finalize() => PostJson(Client, Root + "/finalize", null, csrf, cookie: Token);
        public async Task<HttpResponseMessage> Status()
        {
            // The test clock lives in the past, so the browser-side cookie jar would drop the (already "expired")
            // cookie; the capability is therefore replayed explicitly, exactly as a browser would send it.
            using var request = new HttpRequestMessage(HttpMethod.Get, Root + "/status");
            request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={Token}");
            return await Client.SendAsync(request);
        }
    }


    private sealed class Harness(string connection, int? maxMemories, int ipCreate, int ipIntent, int ipFinalize,
        int perInvitationIntent, bool synchronizeIdempotencyLookups) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Code { get; } = new CryptographicPublicCodeGenerator().Generate();
        public string Email { get; } = $"guest-upload-{Guid.NewGuid():N}@example.test";
        public MutableClock Clock { get; } = new();
        public FakeProvider Provider { get; } = new();
        public FakeGuestMediaUploadAvailability MediaAvailability { get; } = new(true);
        public IdempotencyLookupBarrier IdempotencyBarrier { get; } = new();

        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

        public HttpClient Guest() => CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

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
                ["MemoryUploadCapabilities:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(100, 32).Select(value => (byte)value).ToArray()),
                ["MemoryUploadCapabilities:HmacKeyVersion"] = "1",
                ["MemoryValidation:MaxMemoriesPerInvitation"] = (maxMemories ?? MemoryInputLimits.EngineeringDefaultMaxMemoriesPerInvitation).ToString(),
                ["MediaLifecycleJobs:Enabled"] = "false",
                ["AuthRateLimits:PublicMemorySubmission:PermitLimit"] = "1000",
                ["AuthRateLimits:PublicMemorySubmissionPerInvitation:PermitLimit"] = "1000",
                ["AuthRateLimits:PublicMemoryUploadCreate:PermitLimit"] = ipCreate.ToString(),
                ["AuthRateLimits:PublicMemoryUploadIntent:PermitLimit"] = ipIntent.ToString(),
                ["AuthRateLimits:PublicMemoryUploadFinalize:PermitLimit"] = ipFinalize.ToString(),
                ["AuthRateLimits:PublicMemoryUploadIntentPerInvitation:PermitLimit"] = perInvitationIntent.ToString(),
                ["AuthRateLimits:PublicInvitationRead:PermitLimit"] = "1000",
                ["AuthRateLimits:AntiforgeryToken:PermitLimit"] = "1000"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(Clock);
                services.RemoveAll<IGuestMediaUploadAvailability>();
                services.AddSingleton<IGuestMediaUploadAvailability>(MediaAvailability);
                services.AddSingleton<IMediaUploadGateway>(Provider);
                services.AddSingleton<IMediaImageNormalizationPipeline>(Provider);
                services.AddSingleton<IMediaProviderAssetMaintenance>(new FakeMaintenance());
                if (synchronizeIdempotencyLookups)
                {
                    services.RemoveAll<IGuestMediaStore>();
                    services.AddScoped<IGuestMediaStore>(provider => new BarrierGuestMediaStore(
                        new GuestMediaStore(provider.GetRequiredService<DavetiyeDbContext>(),
                            provider.GetRequiredService<IOutboxWorkStore>()), IdempotencyBarrier));
                }
            });
        }

        public async Task<GuestSession> NewGuestAsync(string? text, GuestTarget? target = null, string? displayName = null)
        {
            var code = target?.Code ?? Code;
            var client = Guest();
            var csrf = await Csrf(client);
            using var response = await PostJson(client, $"/api/v1/public/invitations/{code}/memories/with-media",
                new { displayName, text }, csrf);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));
            var token = setCookie.Split(';', 2)[0][(CookieName.Length + 1)..];
            var body = await response.Content.ReadAsStringAsync();
            var memoryId = JsonDocument.Parse(body).RootElement.GetProperty("memoryId").GetGuid();
            return new GuestSession(code, client, csrf, memoryId, token, setCookie, body);
        }

        /// <summary>Sends a request through a fresh client carrying only the given raw cookie value (a stolen/foreign capability).</summary>
        public async Task<HttpResponseMessage> Probe(string code, Guid memoryId, string kind, string? token, object? body = null)
        {
            using var client = Guest();
            var root = $"/api/v1/public/invitations/{code}/memories/{memoryId}";
            if (kind == "status")
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, root + "/status");
                if (token is not null) request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={token}");
                return await client.SendAsync(request);
            }

            var csrf = await Csrf(client);
            return kind == "intent"
                ? await PostJson(client, root + "/media/intents", body, csrf, Guid.NewGuid(), token)
                : await PostJson(client, root + "/finalize", null, csrf, cookie: token);
        }

        public async Task<GuestTarget> CreateSecondInvitationAsync()
        {
            var accountId = Guid.NewGuid();
            var invitationId = Guid.NewGuid();
            var code = new CryptographicPublicCodeGenerator().Generate();
            var email = $"guest-upload-other-{Guid.NewGuid():N}@example.test";
            await using (var db = Db())
            {
                await AddCreatorAsync(db, accountId, invitationId, code, email);
                await db.SaveChangesAsync();
            }

            await Publish(accountId, invitationId, scheduled: false, paid: true);
            await SeedConfiguration(invitationId, enabled: true);
            return new GuestTarget(accountId, invitationId, code, email);
        }

        public async Task AddCreatorAsync(DavetiyeDbContext db, Guid accountId, Guid invitationId, string code, string email)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Upload tester", Now));
            db.AddAcknowledgedServiceNotice(accountId, Now);
            var invitation = Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, accountId, code, Now);
            invitation.PinTemplate("zamansiz-dugun", 1);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), invitationId, 1, Content, Now));
            await Task.CompletedTask;
        }

        public async Task<HttpClient> LoginAsync(string email)
        {
            var client = Guest();
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }

        public async Task ReadyInvitation()
        {
            await Publish(Account, Invitation, scheduled: false, paid: true);
            await SeedConfiguration(Invitation, enabled: true);
        }

        public async Task Publish(Guid account, Guid invitation, bool scheduled, bool paid)
        {
            Guid? grantId = null;
            if (paid)
            {
                await using var db = Db();
                var grant = AccountPlanGrant.Create(Guid.NewGuid(), account, (await db.Plans.SingleAsync(p => p.Key == "premium")).Id, GrantSource.IndividualPurchase, Now);
                db.AccountPlanGrants.Add(grant);
                await db.SaveChangesAsync();
                grantId = grant.Id;
            }
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(account, invitation, default)).Status!;
            var times = new PublicationWindowRequest(scheduled ? "Scheduled" : "Immediate", scheduled ? "2026-10-03T15:00:00" : null,
                scheduled ? "2026-10-04T15:00:00" : "2026-10-03T15:00:00", "Europe/Istanbul", grantId);
            var result = await service.ExecuteAsync(account, invitation, new("publish", status.Expected, times, ProceedWithRecommendedWarnings: true), default);
            Assert.Equal("Succeeded", result.Code);
        }

        public async Task Command(string action)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, Invitation, new(action, status.Expected), default)).Code);
        }

        public async Task SeedConfiguration(Guid invitation, bool enabled)
        {
            await using var db = Db();
            var configuration = MemoryConfiguration.Create(Guid.NewGuid(), invitation, Now);
            configuration.SetEnabled(enabled, Now);
            configuration.SetVisibility(MemoryVisibility.Public, Now);
            db.MemoryConfigurations.Add(configuration);
            await db.SaveChangesAsync();
        }

        public async Task SetConfiguration(Guid invitation, bool enabled)
        {
            await using var db = Db();
            var configuration = await db.MemoryConfigurations.SingleAsync(c => c.InvitationId == invitation);
            configuration.SetEnabled(enabled, Now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        public async Task SetGuestLimits(long images, long videos)
        {
            await using var db = Db();
            await db.PlanEntitlements.Where(e => e.EntitlementKey == EntitlementCatalog.MaxGuestImages)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NumericValue, images));
            await db.PlanEntitlements.Where(e => e.EntitlementKey == EntitlementCatalog.MaxGuestVideos)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NumericValue, videos));
        }

        public async Task SeedCreatorAsset()
        {
            await using var db = Db();
            db.MediaAssets.Add(MediaAsset.CreateCreatorAsset(Guid.NewGuid(), Invitation, MediaKind.Image, Now));
            db.MediaAssets.Add(MediaAsset.CreateCreatorAsset(Guid.NewGuid(), Invitation, MediaKind.Image, Now));
            await db.SaveChangesAsync();
        }
    }

    private sealed record GuestTarget(Guid AccountId, Guid InvitationId, string Code, string Email);
}
