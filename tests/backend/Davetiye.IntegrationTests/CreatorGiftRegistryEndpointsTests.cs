using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
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

[Collection(PostgreSqlCollection.Name)]
public sealed class CreatorGiftRegistryEndpointsTests(PostgreSqlFixture postgreSql)
{
    private const string Origin = "https://allowed.example.test";
    private const string Password = "TestPassw0rd1";
    // Anchored to the real date: the guest gift cookie expires at the publication window end, and the
    // HTTP client's cookie container evaluates that expiry against the real clock, not the fixed one.
    private static readonly DateTimeOffset Now = new(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);
    private static readonly string Content = $$"""{"eventType":"dugun","headline":"Gift registry","startsAt":"{{Now.AddDays(5).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)}}","timeZoneId":"Europe/Istanbul","venue":{"name":"Venue","address":"Address"},"message":"Welcome","hostNames":["Ada"]}""";

    [Fact]
    public async Task Creator_can_manage_ordered_items_but_cannot_delete_reserved_items_or_access_foreign_items()
    {
        await using var h = await CreateAsync();
        using var creator = await h.LoginAsync(h.Email);
        await h.AddOtherAsync();
        using var other = await h.LoginAsync(h.OtherEmail);
        var root = $"/api/v1/invitations/{h.Invitation}/gifts";

        using var empty = await creator.GetAsync(root);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.True(empty.Headers.CacheControl?.NoStore);
        Assert.Empty(await empty.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);

        using var createA = await WriteAsync(creator, HttpMethod.Post, root, new { name = " Tea set ", requestedQuantity = 4 });
        using var createB = await WriteAsync(creator, HttpMethod.Post, root, new { name = "Vase", requestedQuantity = 2 });
        Assert.Equal(HttpStatusCode.Created, createA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createB.StatusCode);
        var a = await createA.Content.ReadFromJsonAsync<ItemResponse>();
        var b = await createB.Content.ReadFromJsonAsync<ItemResponse>();
        Assert.NotNull(a); Assert.NotNull(b);
        Assert.Equal("Tea set", a.Name);

        using var reorder = await WriteAsync(creator, HttpMethod.Put, root + "/order",
            new { items = new[] { new { id = b.Id, revision = b.Revision }, new { id = a.Id, revision = a.Revision } } });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var reordered = Assert.IsType<ItemResponse[]>(await reorder.Content.ReadFromJsonAsync<ItemResponse[]>());
        Assert.Equal(new[] { b.Id, a.Id }, reordered!.Select(item => item.Id));
        Assert.Equal(new[] { 0, 1 }, reordered.Select(item => item.Ordinal));

        using var update = await WriteAsync(creator, HttpMethod.Put, $"{root}/{a.Id}",
            new { name = "Tea set deluxe", requestedQuantity = 3, expectedRevision = reordered[1].Revision });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ItemResponse>();
        Assert.Equal(3, updated!.RequestedQuantity);

        await h.SeedReservationAsync(a.Id);
        using var reservedDelete = await WriteAsync(creator, HttpMethod.Delete, $"{root}/{a.Id}?expectedRevision={updated.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, reservedDelete.StatusCode);
        using var reducedBelowReserved = await WriteAsync(creator, HttpMethod.Put, $"{root}/{a.Id}",
            new { name = updated.Name, requestedQuantity = 1, expectedRevision = updated.Revision });
        Assert.Equal(HttpStatusCode.BadRequest, reducedBelowReserved.StatusCode);

        using var foreignList = await other.GetAsync(root);
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        using var foreignEdit = await WriteAsync(other, HttpMethod.Put, $"{root}/{a.Id}",
            new { name = "Owned by another", requestedQuantity = 10, expectedRevision = updated.Revision });
        Assert.Equal(HttpStatusCode.NotFound, foreignEdit.StatusCode);

        await h.RemoveReservationAsync();
        using var deleted = await WriteAsync(creator, HttpMethod.Delete, $"{root}/{a.Id}?expectedRevision={updated.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Public_gift_projection_hides_guest_identity_and_same_browser_can_cancel_partial_reservation()
    {
        await using var h = await CreateAsync();
        await h.Publish(paid: true);
        using var creator = await h.LoginAsync(h.Email);
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var itemRoot = $"/api/v1/invitations/{h.Invitation}/gifts";
        using var created = await WriteAsync(creator, HttpMethod.Post, itemRoot, new { name = "Vazo", requestedQuantity = 4 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<ItemResponse>();
        Assert.NotNull(item);
        var publicRoot = $"/api/v1/public/invitations/{h.PublicCode}/gifts";

        using var projection = await guest.GetAsync(publicRoot);
        Assert.Equal(HttpStatusCode.OK, projection.StatusCode);
        var projectionJson = await projection.Content.ReadAsStringAsync();
        Assert.DoesNotContain("guestFullName", projectionJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", projectionJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Vazo", projectionJson);

        await h.SetGiftEntitlementAsync(false);
        using var hiddenByDowngrade = await guest.GetAsync(publicRoot);
        Assert.Equal(HttpStatusCode.NotFound, hiddenByDowngrade.StatusCode);
        using var deniedByDowngrade = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Misafir" });
        Assert.Equal(HttpStatusCode.NotFound, deniedByDowngrade.StatusCode);
        using var creatorPreserved = await creator.GetAsync(itemRoot);
        Assert.Single(await creatorPreserved.Content.ReadFromJsonAsync<ItemResponse[]>() ?? []);
        await h.SetGiftEntitlementAsync(true);

        using var invalidEmail = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Misafir", email = "a" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);

        using var reserved = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 2, fullName = "Misafir Örnek", email = "guest@example.test", phone = "5551234567" });
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        var reservationBody = await reserved.Content.ReadFromJsonAsync<JsonElement>();
        var reservationId = reservationBody.GetProperty("reservationId").GetGuid();
        Assert.False(reservationBody.TryGetProperty("fullName", out _));
        using var redactedProjection = await guest.GetAsync(publicRoot);
        var redactedJson = await redactedProjection.Content.ReadAsStringAsync();
        Assert.DoesNotContain("guestFullName", redactedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Misafir", redactedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("guest@example.test", redactedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("5551234567", redactedJson, StringComparison.Ordinal);
        using var ownReservations = await guest.GetAsync(publicRoot + "/reservations");
        var ownReservation = Assert.Single(await ownReservations.Content.ReadFromJsonAsync<GuestReservationResponse[]>() ?? []);
        Assert.Equal(reservationId, ownReservation.ReservationId);
        Assert.Equal("Vazo", ownReservation.ItemName);

        using var foreignGuest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        using var foreignCreated = await WriteAsync(foreignGuest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Başka Misafir" });
        Assert.Equal(HttpStatusCode.Created, foreignCreated.StatusCode);
        using var foreignDelete = await WriteAsync(foreignGuest, HttpMethod.Delete, publicRoot + $"/reservations/{reservationId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        var foreignId = (await foreignCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservationId").GetGuid();
        using var cancelForeignOwn = await WriteAsync(foreignGuest, HttpMethod.Delete, publicRoot + $"/reservations/{foreignId}");
        Assert.Equal(HttpStatusCode.NoContent, cancelForeignOwn.StatusCode);

        using var afterReserve = await guest.GetAsync(publicRoot);
        Assert.Contains("2", await afterReserve.Content.ReadAsStringAsync());
        using var creatorReservations = await creator.GetAsync(itemRoot + "/reservations");
        var identities = Assert.IsType<ReservationResponse[]>(await creatorReservations.Content.ReadFromJsonAsync<ReservationResponse[]>());
        Assert.Equal("Misafir Örnek", Assert.Single(identities).GuestFullName);
        Assert.Equal("guest@example.test", identities[0].Email);

        await h.SetGiftEntitlementAsync(false);
        using var hiddenOwnedReservations = await guest.GetAsync(publicRoot + "/reservations");
        Assert.Equal(HttpStatusCode.NotFound, hiddenOwnedReservations.StatusCode);
        using var retainedForCreator = await creator.GetAsync(itemRoot + "/reservations");
        var retainedIdentity = Assert.Single(await retainedForCreator.Content.ReadFromJsonAsync<ReservationResponse[]>() ?? []);
        Assert.Equal("guest@example.test", retainedIdentity.Email);
        using var hiddenListAfterReservation = await guest.GetAsync(publicRoot);
        Assert.Equal(HttpStatusCode.NotFound, hiddenListAfterReservation.StatusCode);
        using var deniedOrigin = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Another" }, origin: "https://untrusted.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, deniedOrigin.StatusCode);
        using var missingCsrf = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Another" }, withCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using var cancelled = await WriteAsync(guest, HttpMethod.Delete, publicRoot + $"/reservations/{reservationId}");
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var erasedForCreator = await creator.GetAsync(itemRoot + "/reservations");
        Assert.Empty(await erasedForCreator.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
        await h.SetGiftEntitlementAsync(true);
        using var afterCancel = await guest.GetAsync(publicRoot);
        Assert.Contains("4", await afterCancel.Content.ReadAsStringAsync());
        using var repeated = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "Misafir Örnek" });
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        using var afterRepeat = await guest.GetAsync(publicRoot + "/reservations");
        Assert.Single(await afterRepeat.Content.ReadFromJsonAsync<GuestReservationResponse[]>() ?? []);
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        var repeatedId = repeatedBody.GetProperty("reservationId").GetGuid();
        using var repeatedCancel = await WriteAsync(guest, HttpMethod.Delete, publicRoot + $"/reservations/{repeatedId}");
        Assert.Equal(HttpStatusCode.NoContent, repeatedCancel.StatusCode);
        using var noReservations = await creator.GetAsync(itemRoot + "/reservations");
        Assert.Empty(await noReservations.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);

        using var guestA = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        using var guestB = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var race = await Task.WhenAll(
            WriteAsync(guestA, HttpMethod.Post, publicRoot + "/reservations", new { itemId = item!.Id, quantity = 3, fullName = "Guest A" }),
            WriteAsync(guestB, HttpMethod.Post, publicRoot + "/reservations", new { itemId = item!.Id, quantity = 3, fullName = "Guest B" }));
        using var firstRace = race[0];
        using var secondRace = race[1];
        Assert.Single(race, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(race, response => response.StatusCode == HttpStatusCode.BadRequest);
        using var afterRace = await guest.GetAsync(publicRoot);
        Assert.Contains("1", await afterRace.Content.ReadAsStringAsync());
        using var racedReservations = await creator.GetAsync(itemRoot + "/reservations");
        var raced = Assert.Single(await racedReservations.Content.ReadFromJsonAsync<ReservationResponse[]>() ?? []);
        using var creatorRemoved = await WriteAsync(creator, HttpMethod.Delete, itemRoot + $"/reservations/{raced.Id}");
        Assert.Equal(HttpStatusCode.NoContent, creatorRemoved.StatusCode);
        using var afterCreatorRemoval = await guest.GetAsync(publicRoot);
        Assert.Contains("4", await afterCreatorRemoval.Content.ReadAsStringAsync());
        await h.Command("pause");
        using var pausedProjection = await guest.GetAsync(publicRoot);
        Assert.Equal(HttpStatusCode.NotFound, pausedProjection.StatusCode);
        using var pausedReserve = await WriteAsync(guest, HttpMethod.Post, publicRoot + "/reservations",
            new { itemId = item!.Id, quantity = 1, fullName = "After pause" });
        Assert.Equal(HttpStatusCode.NotFound, pausedReserve.StatusCode);
    }

    [Fact]
    public async Task Public_gift_reservations_are_rate_limited()
    {
        await using var h = await CreateAsync(publicWriteLimit: 1);
        await h.Publish(paid: true);
        using var creator = await h.LoginAsync(h.Email);
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var itemRoot = $"/api/v1/invitations/{h.Invitation}/gifts";
        using var created = await WriteAsync(creator, HttpMethod.Post, itemRoot, new { name = "Kitap", requestedQuantity = 5 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<ItemResponse>();
        var publicRoot = $"/api/v1/public/invitations/{h.PublicCode}/gifts/reservations";
        using var first = await WriteAsync(guest, HttpMethod.Post, publicRoot,
            new { itemId = item!.Id, quantity = 1, fullName = "Guest" });
        using var limited = await WriteAsync(guest, HttpMethod.Post, publicRoot,
            new { itemId = item.Id, quantity = 1, fullName = "Guest" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, HttpMethod method, string path, object? body = null,
        bool withCsrf = true, string? origin = Origin)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (withCsrf) request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.SendAsync(request);
    }

    private sealed record CsrfResponse(string Token);
    private sealed record ItemResponse(Guid Id, string Name, int RequestedQuantity, int ReservedQuantity,
        int RemainingQuantity, int Ordinal, long Revision);
    private sealed record ReservationResponse(Guid Id, Guid ItemId, int Quantity, string GuestFullName, string? Email, string? Phone, DateTimeOffset CreatedAt);
    private sealed record GuestReservationResponse(Guid ReservationId, Guid ItemId, string ItemName, int Quantity);
    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token"))!.Token;

    private async Task<Harness> CreateAsync(int publicWriteLimit = 100)
    {
        var connection = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false }.ConnectionString;
        var h = new Harness(connection, publicWriteLimit);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        await h.AddCreatorAsync(db, h.Account, h.Invitation, h.Email);
        await db.SaveChangesAsync();
        return h;
    }

    private sealed class Harness(string connection, int publicWriteLimit) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string PublicCode { get; private set; } = string.Empty;
        public string Email { get; } = $"gift-{Guid.NewGuid():N}@example.test";
        public string OtherEmail { get; } = $"gift-other-{Guid.NewGuid():N}@example.test";
        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = Origin,
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                ["RsvpCapabilities:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
                ["RsvpCapabilities:HmacKeyVersion"] = "1",
                ["GiftCapabilities:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
                ["GiftCapabilities:HmacKeyVersion"] = "1",
                ["AuthRateLimits:PublicRsvpSubmission:PermitLimit"] = publicWriteLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Davetiye.Domain.Modules.SharedKernel.IClock>();
                services.AddSingleton<Davetiye.Domain.Modules.SharedKernel.IClock>(new FixedClock());
            });
        }

        public async Task AddCreatorAsync(DavetiyeDbContext db, Guid accountId, Guid invitationId, string email)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Gift tester", Now));
            db.AddAcknowledgedServiceNotice(accountId, Now);
            var code = new CryptographicPublicCodeGenerator().Generate();
            var invitation = Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, accountId, code, Now);
            if (invitationId == Invitation) PublicCode = code;
            invitation.PinTemplate("romantik-nisan", 1);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), invitationId, 1, Content, Now));
        }

        public async Task AddOtherAsync()
        {
            await using var db = Db();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = OtherEmail, NormalizedUserName = OtherEmail.ToUpperInvariant(),
                Email = OtherEmail, NormalizedEmail = OtherEmail.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            var accountId = Guid.NewGuid();
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Other", Now));
            db.AddAcknowledgedServiceNotice(accountId, Now);
            await db.SaveChangesAsync();
        }

        public async Task SeedReservationAsync(Guid itemId)
        {
            await using var db = Db();
            var now = Now;
            var session = GuestGiftSession.Create(Guid.NewGuid(), Invitation, GuestGiftSession.RequiredPurpose, 1,
                new byte[GuestGiftSession.HmacSha256DigestLength], now);
            db.GuestGiftSessions.Add(session);
            db.GiftReservations.Add(GiftReservation.Create(Guid.NewGuid(), Invitation, itemId, session.Id, 2, "Ada Guest", null, null, now));
            await db.SaveChangesAsync();
        }

        public async Task RemoveReservationAsync()
        {
            await using var db = Db();
            await db.GiftReservations.Where(value => value.InvitationId == Invitation).ExecuteDeleteAsync();
        }

        public async Task SetGiftEntitlementAsync(bool enabled)
        {
            await using var db = Db();
            var grantId = await db.PublicationWindows.Where(value => value.InvitationId == Invitation && value.IsCurrent)
                .Select(value => value.GrantId).SingleAsync();
            var planId = await db.AccountPlanGrants.Where(value => value.Id == grantId).Select(value => value.PlanId).SingleAsync();
            var entitlement = await db.PlanEntitlements.SingleAsync(value => value.PlanId == planId &&
                value.EntitlementKey == EntitlementCatalog.GiftRegistryEnabled);
            entitlement.UpdateValue(null, enabled);
            await db.SaveChangesAsync();
        }

        public async Task Publish(bool paid)
        {
            Guid? grantId = null;
            if (paid)
            {
                await using var db = Db();
                var grant = AccountPlanGrant.Create(Guid.NewGuid(), Account,
                    (await db.Plans.SingleAsync(plan => plan.Key == "premium")).Id, GrantSource.IndividualPurchase, Now);
                db.AccountPlanGrants.Add(grant);
                await db.SaveChangesAsync();
                grantId = grant.Id;
            }
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            var window = new PublicationWindowRequest("Immediate", null,
                Now.AddDays(2).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                "Europe/Istanbul", grantId);
            var result = await service.ExecuteAsync(Account, Invitation,
                new("publish", status.Expected, window, ProceedWithRecommendedWarnings: true), default);
            Assert.Equal("Succeeded", result.Code);
        }

        public async Task Command(string action)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            var result = await service.ExecuteAsync(Account, Invitation, new(action, status.Expected), default);
            Assert.Equal("Succeeded", result.Code);
        }

        public async Task<HttpClient> LoginAsync(string email)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }
    }

    private sealed class FixedClock : Davetiye.Domain.Modules.SharedKernel.IClock { public DateTimeOffset UtcNow => Now; }
}
