using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// Real-PostgreSQL integration tests for the authenticated <c>GET/PUT /api/v1/account/preferences</c>
/// routes (Phase 11 account UI preferences): authentication, antiforgery, per-user isolation,
/// server-side validation, DB CHECK constraints, per-user write rate limiting, no-store caching,
/// the service-notice gate and the production HTTPS rule. No sleeps, fixed dates or locale
/// dependence: rate limits are shrunk through configuration and every factory owns its limiter.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AccountUiPreferencesEndpointsTests(PostgreSqlFixture postgreSql) : IAsyncLifetime
{
    private const string Route = "/api/v1/account/preferences";
    private const string Password = "TestPassw0rd1";

    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_get_and_put_return_401_with_no_store()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var get = await client.GetAsync(Route);
        using var put = await SendPutAsync(client, new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = (string?)null }, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.True(put.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Put_without_or_with_a_bad_antiforgery_token_returns_400_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);
        var validToken = await GetCsrfTokenAsync(client);
        var body = new { locale = "en", colorTheme = "ocean", appearance = "dark", avatar = (string?)null };

        using var missing = await SendPutAsync(client, body, token: null);
        using var bad = await SendPutAsync(client, body, token: "not-a-valid-antiforgery-token");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True(missing.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True(bad.Headers.CacheControl?.NoStore);
        await AssertStoredAsync(creator.UserId, "tr", "kutlio", "light");

        // The same session with the genuine token still works, proving the 400s were token-driven.
        using var ok = await SendPutAsync(client, body, validToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await AssertStoredAsync(creator.UserId, "en", "ocean", "dark");
    }

    [Fact]
    public async Task One_users_update_is_never_visible_to_another_and_forged_identifiers_are_ignored()
    {
        await using var factory = CreateFactory();
        var userA = await SeedCreatorAsync(acknowledged: true);
        var userB = await SeedCreatorAsync(acknowledged: true);
        using var clientA = await LoginAsync(factory, userA.Email);
        using var clientB = await LoginAsync(factory, userB.Email);
        var tokenA = await GetCsrfTokenAsync(clientA);

        // Forged identifiers in the body and the query string must not redirect the write.
        using var forgedBodyAndQuery = await SendPutAsync(
            clientA,
            new
            {
                locale = "en",
                colorTheme = "plum",
                appearance = "dark",
                avatar = "coral",
                identityUserId = userB.UserId,
                userId = userB.UserId,
                id = userB.UserId,
            },
            tokenA,
            $"{Route}?identityUserId={userB.UserId}&userId={userB.UserId}");
        Assert.Equal(HttpStatusCode.OK, forgedBodyAndQuery.StatusCode);

        // A forged identifier in the route addresses no endpoint at all.
        using var forgedRoute = await SendPutAsync(
            clientA,
            new { locale = "en", colorTheme = "rose", appearance = "light", avatar = (string?)null },
            tokenA,
            $"{Route}/{userB.UserId}");
        Assert.False(forgedRoute.IsSuccessStatusCode);

        using var getB = await clientB.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        Assert.Equal(("tr", "kutlio", "light"), await ReadPreferencesAsync(getB));

        using var getA = await clientA.GetAsync(Route);
        Assert.Equal(("en", "plum", "dark"), await ReadPreferencesAsync(getA));

        await AssertStoredAsync(userA.UserId, "en", "plum", "dark");
        await AssertStoredAsync(userB.UserId, "tr", "kutlio", "light");
    }

    [Fact]
    public async Task Super_admin_without_a_domain_account_can_read_and_update_preferences()
    {
        await using var factory = CreateFactory();
        const string email = "ui-prefs-admin@example.test";
        Guid adminUserId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(
                AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(users, db).RunAsync(email, Password, CancellationToken.None));
            adminUserId = (await users.FindByEmailAsync(email))!.Id;
        }

        await using (var db = CreateDbContext())
            Assert.False(await db.Accounts.AnyAsync(account => account.IdentityUserId == adminUserId));

        using var client = factory.CreateClient();
        await CompleteAdminMfaAsync(client, email, Password);

        using var before = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(("tr", "kutlio", "light"), await ReadPreferencesAsync(before));

        var token = await GetCsrfTokenAsync(client);
        using var update = await SendPutAsync(client, new { locale = "en", colorTheme = "rose", appearance = "light", avatar = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(("en", "rose", "light"), await ReadPreferencesAsync(update));

        using var after = await client.GetAsync(Route);
        Assert.Equal(("en", "rose", "light"), await ReadPreferencesAsync(after));
        await AssertStoredAsync(adminUserId, "en", "rose", "light");
    }

    [Fact]
    public async Task Invalid_null_missing_and_oversized_values_return_400_and_persist_nothing()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);
        var token = await GetCsrfTokenAsync(client);
        var oversized = new string('a', 100_000);

        var invalidBodies = new Dictionary<string, string>
        {
            ["unknown locale"] = """{"locale":"fr","colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["wrong-case locale"] = """{"locale":"TR","colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["empty locale"] = """{"locale":"","colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["whitespace locale"] = """{"locale":" ","colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["padded locale"] = """{"locale":"tr ","colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["null locale"] = """{"locale":null,"colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["missing locale"] = """{"colorTheme":"kutlio","appearance":"system","avatar":null}""",
            ["unknown theme"] = """{"locale":"tr","colorTheme":"purple","appearance":"system","avatar":null}""",
            ["empty theme"] = """{"locale":"tr","colorTheme":"","appearance":"system","avatar":null}""",
            ["null theme"] = """{"locale":"tr","colorTheme":null,"appearance":"system","avatar":null}""",
            ["missing theme"] = """{"locale":"tr","appearance":"system","avatar":null}""",
            ["unknown appearance"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"bright","avatar":null}""",
            ["empty appearance"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"","avatar":null}""",
            ["null appearance"] = """{"locale":"tr","colorTheme":"kutlio","appearance":null,"avatar":null}""",
            ["missing appearance"] = """{"locale":"tr","colorTheme":"kutlio","avatar":null}""",
            ["missing avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system"}""",
            ["wrong-case avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":"Sunny"}""",
            ["leading-space avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":" sunny"}""",
            ["trailing-space avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":"sunny "}""",
            ["unknown avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":"unknown"}""",
            ["empty avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":""}""",
            ["numeric avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":1}""",
            ["object avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":{"key":"sunny"}}""",
            ["array avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":["sunny"]}""",
            ["boolean avatar"] = """{"locale":"tr","colorTheme":"kutlio","appearance":"system","avatar":true}""",
            ["oversized avatar"] = JsonSerializer.Serialize(new { locale = "tr", colorTheme = "kutlio", appearance = "system", avatar = oversized }),
            ["sql-shaped avatar"] = JsonSerializer.Serialize(new { locale = "tr", colorTheme = "kutlio", appearance = "system", avatar = "sunny'; DROP TABLE asp_net_users;--" }),
            ["empty object"] = "{}",
            ["oversized locale"] = JsonSerializer.Serialize(new { locale = oversized, colorTheme = "kutlio", appearance = "system", avatar = (string?)null }),
            ["oversized theme"] = JsonSerializer.Serialize(new { locale = "tr", colorTheme = oversized, appearance = "system", avatar = (string?)null }),
            ["oversized appearance"] = JsonSerializer.Serialize(new { locale = "tr", colorTheme = "kutlio", appearance = oversized, avatar = (string?)null }),
            ["sql-shaped value"] = JsonSerializer.Serialize(new { locale = "tr'; DROP TABLE asp_net_users;--", colorTheme = "kutlio", appearance = "system", avatar = (string?)null }),
        };

        foreach (var (name, json) in invalidBodies)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, Route)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using var response = await client.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: expected 400 but was {(int)response.StatusCode}.");
            Assert.True(response.Headers.CacheControl?.NoStore, $"{name}: expected Cache-Control no-store.");
        }

        await AssertStoredAsync(creator.UserId, "tr", "kutlio", "light");
        using var get = await client.GetAsync(Route);
        Assert.Equal(("tr", "kutlio", "light"), await ReadPreferencesAsync(get));
    }

    [Fact]
    public async Task Database_check_constraints_reject_unsupported_values_even_when_the_api_is_bypassed()
    {
        await SeedCreatorAsync(acknowledged: true);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Control: a valid statement succeeds, so the failures below are caused by the constraints.
        await using (var valid = new NpgsqlCommand("UPDATE asp_net_users SET preferred_locale = 'en'", connection))
            Assert.True(await valid.ExecuteNonQueryAsync() >= 1);
        await using (var validAvatar = new NpgsqlCommand("UPDATE asp_net_users SET preferred_avatar = 'peach'", connection))
            Assert.True(await validAvatar.ExecuteNonQueryAsync() >= 1);
        await using (var clearedAvatar = new NpgsqlCommand("UPDATE asp_net_users SET preferred_avatar = NULL", connection))
            Assert.True(await clearedAvatar.ExecuteNonQueryAsync() >= 1);

        var cases = new (string Sql, string Constraint)[]
        {
            ("UPDATE asp_net_users SET preferred_locale = 'fr'", "ck_asp_net_users_preferred_locale"),
            ("UPDATE asp_net_users SET preferred_color_theme = 'purple'", "ck_asp_net_users_preferred_color_theme"),
            ("UPDATE asp_net_users SET preferred_appearance = 'bright'", "ck_asp_net_users_preferred_appearance"),
            ("UPDATE asp_net_users SET preferred_avatar = 'unknown'", "ck_asp_net_users_preferred_avatar"),
            ("UPDATE asp_net_users SET preferred_avatar = 'Sunny'", "ck_asp_net_users_preferred_avatar"),
            ("UPDATE asp_net_users SET preferred_avatar = ''", "ck_asp_net_users_preferred_avatar"),
        };

        foreach (var (sql, constraint) in cases)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal(constraint, exception.ConstraintName);
        }
    }

    [Fact]
    public async Task Database_default_appearance_is_light_for_new_rows_while_system_and_dark_stay_valid()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // (a) A raw INSERT that omits preferred_appearance gets the column default, 'light'.
        var defaulted = Guid.NewGuid();
        await InsertRawUserAsync(connection, defaulted, appearance: null);
        Assert.Equal("light", await ReadRawAppearanceAsync(connection, defaulted));

        // (b) An explicit 'system' is accepted on INSERT and kept, and a later UPDATE to it is kept too.
        var explicitSystem = Guid.NewGuid();
        await InsertRawUserAsync(connection, explicitSystem, appearance: "system");
        Assert.Equal("system", await ReadRawAppearanceAsync(connection, explicitSystem));

        await using (var toDark = new NpgsqlCommand(
            "UPDATE asp_net_users SET preferred_appearance = 'dark' WHERE id = @id", connection))
        {
            toDark.Parameters.AddWithValue("id", defaulted);
            Assert.Equal(1, await toDark.ExecuteNonQueryAsync());
        }

        // (c) 'dark' is accepted.
        Assert.Equal("dark", await ReadRawAppearanceAsync(connection, defaulted));

        await using (var toSystem = new NpgsqlCommand(
            "UPDATE asp_net_users SET preferred_appearance = 'system' WHERE id = @id", connection))
        {
            toSystem.Parameters.AddWithValue("id", defaulted);
            Assert.Equal(1, await toSystem.ExecuteNonQueryAsync());
        }

        Assert.Equal("system", await ReadRawAppearanceAsync(connection, defaulted));

        // The allow-list itself is unchanged: an unsupported value is still rejected.
        await using var invalid = new NpgsqlCommand(
            "UPDATE asp_net_users SET preferred_appearance = 'bright' WHERE id = @id", connection);
        invalid.Parameters.AddWithValue("id", defaulted);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_asp_net_users_preferred_appearance", exception.ConstraintName);
    }

    private static async Task InsertRawUserAsync(NpgsqlConnection connection, Guid id, string? appearance)
    {
        var columns = "id, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count";
        var values = "@id, false, false, false, false, 0";
        if (appearance is not null)
        {
            columns += ", preferred_appearance";
            values += ", @appearance";
        }

        await using var insert = new NpgsqlCommand($"INSERT INTO asp_net_users ({columns}) VALUES ({values})", connection);
        insert.Parameters.AddWithValue("id", id);
        if (appearance is not null)
            insert.Parameters.AddWithValue("appearance", appearance);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadRawAppearanceAsync(NpgsqlConnection connection, Guid id)
    {
        await using var select = new NpgsqlCommand(
            "SELECT preferred_appearance FROM asp_net_users WHERE id = @id", connection);
        select.Parameters.AddWithValue("id", id);
        return (string)(await select.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Put_beyond_the_per_user_window_limit_returns_429_without_changing_the_row_and_other_users_are_unaffected()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AuthRateLimits:UiPreferencesWrite:PermitLimit"] = "2",
            ["AuthRateLimits:UiPreferencesWrite:WindowSeconds"] = "3600",
        });
        var userA = await SeedCreatorAsync(acknowledged: true);
        var userB = await SeedCreatorAsync(acknowledged: true);
        using var clientA = await LoginAsync(factory, userA.Email);
        using var clientB = await LoginAsync(factory, userB.Email);
        var tokenA = await GetCsrfTokenAsync(clientA);
        var tokenB = await GetCsrfTokenAsync(clientB);

        using var first = await SendPutAsync(clientA, new { locale = "en", colorTheme = "sage", appearance = "light", avatar = (string?)null }, tokenA);
        using var second = await SendPutAsync(clientA, new { locale = "en", colorTheme = "rose", appearance = "dark", avatar = (string?)null }, tokenA);
        using var third = await SendPutAsync(clientA, new { locale = "tr", colorTheme = "plum", appearance = "system", avatar = (string?)null }, tokenA);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.CacheControl?.NoStore);
        await AssertStoredAsync(userA.UserId, "en", "rose", "dark");

        // The limiter is partitioned per Identity user, so B still has its own budget.
        using var otherUser = await SendPutAsync(clientB, new { locale = "en", colorTheme = "ocean", appearance = "light", avatar = (string?)null }, tokenB);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
        await AssertStoredAsync(userB.UserId, "en", "ocean", "light");
    }

    [Fact]
    public async Task Creator_who_has_not_acknowledged_the_service_notice_gets_428_until_acknowledgement()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: false);
        using var client = await LoginAsync(factory, creator.Email);
        var token = await GetCsrfTokenAsync(client);

        using var get = await client.GetAsync(Route);
        using var put = await SendPutAsync(client, new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = (string?)null }, token);

        Assert.Equal((HttpStatusCode)428, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Equal((HttpStatusCode)428, put.StatusCode);
        Assert.True(put.Headers.CacheControl?.NoStore);
        await AssertStoredAsync(creator.UserId, "tr", "kutlio", "light");

        await using (var db = CreateDbContext())
        {
            db.AddAcknowledgedServiceNotice(creator.AccountId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        using var released = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
    }

    [Fact]
    public async Task New_user_gets_defaults_and_a_successful_put_round_trips_through_get_and_the_database()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);

        using var initial = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.True(initial.Headers.CacheControl?.NoStore);
        Assert.Equal(("tr", "kutlio", "light"), await ReadPreferencesAsync(initial));

        var token = await GetCsrfTokenAsync(client);
        using var update = await SendPutAsync(client, new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True(update.Headers.CacheControl?.NoStore);
        Assert.Equal(("en", "sage", "dark"), await ReadPreferencesAsync(update));

        using var reread = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
        Assert.True(reread.Headers.CacheControl?.NoStore);
        Assert.Equal(("en", "sage", "dark"), await ReadPreferencesAsync(reread));
        await AssertStoredAsync(creator.UserId, "en", "sage", "dark");

        using var overwrite = await SendPutAsync(client, new { locale = "tr", colorTheme = "plum", appearance = "light", avatar = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, overwrite.StatusCode);
        await AssertStoredAsync(creator.UserId, "tr", "plum", "light");
    }

    [Fact]
    public async Task Avatar_defaults_to_null_and_set_and_clear_round_trip_through_get_and_the_database()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);

        using var initial = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Null(await ReadAvatarAsync(initial));
        await AssertStoredAvatarAsync(creator.UserId, null);

        var token = await GetCsrfTokenAsync(client);
        using var set = await SendPutAsync(client, new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = "berry" }, token);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("berry", await ReadAvatarAsync(set));

        using var reread = await client.GetAsync(Route);
        Assert.Equal("berry", await ReadAvatarAsync(reread));
        Assert.Equal(("en", "sage", "dark"), await ReadPreferencesAsync(reread));
        await AssertStoredAvatarAsync(creator.UserId, "berry");

        using var clear = await SendPutAsync(client, new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null(await ReadAvatarAsync(clear));
        using var afterClear = await client.GetAsync(Route);
        Assert.Null(await ReadAvatarAsync(afterClear));
        await AssertStoredAvatarAsync(creator.UserId, null);
    }

    [Fact]
    public async Task Every_one_of_the_twelve_preset_avatar_keys_is_accepted_and_stored()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AuthRateLimits:UiPreferencesWrite:PermitLimit"] = "1000",
        });
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);
        var token = await GetCsrfTokenAsync(client);
        string[] keys = ["sunny", "mint", "berry", "sky", "coral", "lilac", "amber", "forest", "night", "rose", "slate", "peach"];

        foreach (var key in keys)
        {
            using var response = await SendPutAsync(client, new { locale = "tr", colorTheme = "kutlio", appearance = "system", avatar = key }, token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{key}: expected 200 but was {(int)response.StatusCode}.");
            Assert.Equal(key, await ReadAvatarAsync(response));
            await AssertStoredAvatarAsync(creator.UserId, key);
        }
    }

    [Fact]
    public async Task A_rejected_avatar_leaves_the_previously_stored_preferences_unchanged()
    {
        await using var factory = CreateFactory();
        var creator = await SeedCreatorAsync(acknowledged: true);
        using var client = await LoginAsync(factory, creator.Email);
        var token = await GetCsrfTokenAsync(client);

        using var set = await SendPutAsync(client, new { locale = "en", colorTheme = "ocean", appearance = "light", avatar = "night" }, token);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var bad = await SendPutAsync(client, new { locale = "tr", colorTheme = "plum", appearance = "dark", avatar = "Night" }, token);
        using var missing = await SendPutAsync(client, new { locale = "tr", colorTheme = "plum", appearance = "dark" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        await AssertStoredAsync(creator.UserId, "en", "ocean", "light");
        await AssertStoredAvatarAsync(creator.UserId, "night");
    }

    [Fact]
    public async Task One_users_avatar_is_never_visible_to_another_user()
    {
        await using var factory = CreateFactory();
        var userA = await SeedCreatorAsync(acknowledged: true);
        var userB = await SeedCreatorAsync(acknowledged: true);
        using var clientA = await LoginAsync(factory, userA.Email);
        using var clientB = await LoginAsync(factory, userB.Email);
        var tokenA = await GetCsrfTokenAsync(clientA);

        using var update = await SendPutAsync(
            clientA,
            new { locale = "en", colorTheme = "plum", appearance = "dark", avatar = "lilac", identityUserId = userB.UserId },
            tokenA,
            $"{Route}?identityUserId={userB.UserId}");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var getB = await clientB.GetAsync(Route);
        Assert.Null(await ReadAvatarAsync(getB));
        using var getA = await clientA.GetAsync(Route);
        Assert.Equal("lilac", await ReadAvatarAsync(getA));
        await AssertStoredAvatarAsync(userA.UserId, "lilac");
        await AssertStoredAvatarAsync(userB.UserId, null);
    }

    [Fact]
    public async Task Super_admin_without_a_domain_account_can_set_and_clear_an_avatar()
    {
        await using var factory = CreateFactory();
        const string email = "ui-prefs-avatar-admin@example.test";
        Guid adminUserId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(
                AdminBootstrapOutcome.Created,
                await new AdminBootstrapRunner(users, db).RunAsync(email, Password, CancellationToken.None));
            adminUserId = (await users.FindByEmailAsync(email))!.Id;
        }

        await using (var db = CreateDbContext())
            Assert.False(await db.Accounts.AnyAsync(account => account.IdentityUserId == adminUserId));

        using var client = factory.CreateClient();
        await CompleteAdminMfaAsync(client, email, Password);
        var token = await GetCsrfTokenAsync(client);

        using var set = await SendPutAsync(client, new { locale = "tr", colorTheme = "kutlio", appearance = "system", avatar = "slate" }, token);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("slate", await ReadAvatarAsync(set));
        await AssertStoredAvatarAsync(adminUserId, "slate");

        using var clear = await SendPutAsync(client, new { locale = "tr", colorTheme = "kutlio", appearance = "system", avatar = (string?)null }, token);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        await AssertStoredAvatarAsync(adminUserId, null);
    }

    [Fact]
    public async Task Raw_http_requests_are_rejected_in_production_before_antiforgery_while_https_works()
    {
        await using var factory = CreateFactory(environmentName: "Production");
        var creator = await SeedCreatorAsync(acknowledged: true);

        // Login is itself HTTPS-only in Production, so obtain the session over an https base address.
        using var secure = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        using var login = await secure.PostAsJsonAsync("/api/v1/auth/login", new { email = creator.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var cookieHeader = string.Join("; ", setCookies!.Select(value => value.Split(';', 2)[0]));

        using var secureGet = new HttpRequestMessage(HttpMethod.Get, Route);
        secureGet.Headers.Add("Cookie", cookieHeader);
        using var secureResponse = await secure.SendAsync(secureGet);
        Assert.Equal(HttpStatusCode.OK, secureResponse.StatusCode);

        // No X-Forwarded-Proto: indistinguishable from a raw, unproxied HTTP request.
        using var raw = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
        });
        using var rawGet = new HttpRequestMessage(HttpMethod.Get, Route);
        rawGet.Headers.Add("Cookie", cookieHeader);
        using var rawGetResponse = await raw.SendAsync(rawGet);
        Assert.Equal(HttpStatusCode.BadRequest, rawGetResponse.StatusCode);
        Assert.Equal("HTTPS is required for authentication traffic.", await ReadProblemTitleAsync(rawGetResponse));

        // The HTTPS filter must run before the antiforgery filter: no token is sent, yet the
        // rejection is the HTTPS one, not the antiforgery one.
        using var rawPut = new HttpRequestMessage(HttpMethod.Put, Route)
        {
            Content = JsonContent.Create(new { locale = "en", colorTheme = "sage", appearance = "dark", avatar = (string?)null }),
        };
        rawPut.Headers.Add("Cookie", cookieHeader);
        using var rawPutResponse = await raw.SendAsync(rawPut);
        Assert.Equal(HttpStatusCode.BadRequest, rawPutResponse.StatusCode);
        Assert.Equal("HTTPS is required for authentication traffic.", await ReadProblemTitleAsync(rawPutResponse));
        await AssertStoredAsync(creator.UserId, "tr", "kutlio", "light");
    }

    private static async Task<string?> ReadProblemTitleAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    private static async Task<(string Locale, string ColorTheme, string Appearance)> ReadPreferencesAsync(
        HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return (
            root.GetProperty("locale").GetString()!,
            root.GetProperty("colorTheme").GetString()!,
            root.GetProperty("appearance").GetString()!);
    }

    private static async Task<string?> ReadAvatarAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var avatar = json.RootElement.GetProperty("avatar");
        return avatar.ValueKind == JsonValueKind.Null ? null : avatar.GetString();
    }

    private async Task AssertStoredAvatarAsync(Guid identityUserId, string? avatar)
    {
        await using var db = CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == identityUserId);
        Assert.Equal(avatar, user.PreferredAvatar);
    }

    private async Task AssertStoredAsync(Guid identityUserId, string locale, string colorTheme, string appearance)
    {
        await using var db = CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == identityUserId);
        Assert.Equal(locale, user.PreferredLocale);
        Assert.Equal(colorTheme, user.PreferredColorTheme);
        Assert.Equal(appearance, user.PreferredAppearance);
    }

    private static async Task<HttpResponseMessage> SendPutAsync(
        HttpClient client, object body, string? token, string path = Route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private async Task<SeededCreator> SeedCreatorAsync(bool acknowledged)
    {
        var email = $"ui-prefs-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = CreateDbContext();
        db.Users.Add(user);
        db.Accounts.Add(Account.Create(accountId, user.Id, AccountType.Individual, "Prefs tester", now));
        if (acknowledged)
            db.AddAcknowledgedServiceNotice(accountId, now);
        await db.SaveChangesAsync();
        return new SeededCreator(user.Id, accountId, email);
    }

    private sealed record SeededCreator(Guid UserId, Guid AccountId, string Email);

    private PreferencesApiFactory CreateFactory(
        Dictionary<string, string?>? extraConfig = null,
        string environmentName = "Development") =>
        new(connectionString, environmentName, extraConfig);

    private DavetiyeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;
        return new DavetiyeDbContext(options);
    }

    private static async Task CompleteAdminMfaAsync(HttpClient client, string email, string password)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        var enrollToken = await GetCsrfTokenAsync(client);
        using var enroll = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/enroll", new { }, enrollToken);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        using var json = JsonDocument.Parse(await enroll.Content.ReadAsStringAsync());
        var sharedKey = json.RootElement.GetProperty("sharedKey").GetString()!;
        var verifyToken = await GetCsrfTokenAsync(client);
        using var verify = await PostWithCsrfAsync(client, "/api/v1/admin/mfa/verify",
            new { code = GenerateTotpCode(sharedKey) }, verifyToken);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var logoutToken = await GetCsrfTokenAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/v1/auth/logout", new { }, logoutToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        using var complete = await client.PostAsJsonAsync("/api/v1/admin/mfa/login/complete",
            new { code = GenerateTotpCode(sharedKey), isRecoveryCode = false });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
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
            var value = alphabet.IndexOf(character, StringComparison.Ordinal);
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
        var code = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (code % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static async Task RunMigratorAsync(string testConnectionString)
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, "tools", "Davetiye.DatabaseMigrator", "bin", TestBuildConfiguration.Name, "net10.0", "Davetiye.DatabaseMigrator.dll");
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
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class PreferencesApiFactory(
        string connection,
        string environmentName,
        Dictionary<string, string?>? extraConfig) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // Per-IP route limits are raised so that only the per-user preferences limiter can
                // produce a 429 here; tests then shrink that one limiter through extraConfig.
                var values = new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connection,
                    ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                    ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                    ["AuthRateLimits:Login:PermitLimit"] = "1000",
                    ["AuthRateLimits:AntiforgeryToken:PermitLimit"] = "1000",
                    ["AuthRateLimits:PublicationRead:PermitLimit"] = "1000",
                    ["AuthRateLimits:PublicationAction:PermitLimit"] = "1000",
                    ["AuthRateLimits:AdminMfaVerify:PermitLimit"] = "1000",
                    ["AuthRateLimits:TwoFactorLoginComplete:PermitLimit"] = "1000",
                };

                if (extraConfig is not null)
                {
                    foreach (var (key, value) in extraConfig)
                        values[key] = value;
                }

                configuration.AddInMemoryCollection(values);
            });
        }
    }
}
