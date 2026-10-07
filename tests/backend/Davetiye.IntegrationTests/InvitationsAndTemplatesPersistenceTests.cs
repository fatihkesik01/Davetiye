using System.Diagnostics;
using System.Text.Json;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// Real-PostgreSQL proof (Testcontainers) for docs/PHASE_2_PLAN.md M2's schema soundness claims:
/// two different AccountId owners can each own their own Invitation without collision (the
/// query-level ownership filter M3 will build CRUD around actually has something real to filter on),
/// the (TemplateKey, RendererVersion) pin round-trips and stays paired, WorkingContent's explicit
/// revision token rejects a stale concurrent write, and the one-Invitation/one-WorkingContent DB
/// constraint is real. This is not M3's full CRUD/BOLA suite - just proof the M2 schema itself holds.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class InvitationsAndTemplatesPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Two_different_accounts_each_own_their_own_invitation_without_collision()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var invitationA = Invitation.Create(Guid.NewGuid(), accountA, PublicCode('a'), DateTimeOffset.UtcNow);
        var invitationB = Invitation.Create(Guid.NewGuid(), accountB, PublicCode('b'), DateTimeOffset.UtcNow);

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Invitations.AddRange(invitationA, invitationB);
            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = CreateDbContext(connectionString);

        // Query-level ownership filtering, the pattern M3's Creator-owned CRUD is expected to apply
        // on every read/mutation (docs/PHASE_0_PLAN.md §4): each account only ever sees its own row.
        var ownedByA = await queryContext.Invitations
            .Where(invitation => invitation.AccountId == accountA)
            .ToListAsync();
        var ownedByB = await queryContext.Invitations
            .Where(invitation => invitation.AccountId == accountB)
            .ToListAsync();

        Assert.Single(ownedByA);
        Assert.Equal(invitationA.Id, ownedByA[0].Id);
        Assert.Single(ownedByB);
        Assert.Equal(invitationB.Id, ownedByB[0].Id);

        var crossOwnerLookup = await queryContext.Invitations
            .Where(invitation => invitation.Id == invitationA.Id && invitation.AccountId == accountB)
            .ToListAsync();
        Assert.Empty(crossOwnerLookup);
    }

    [Fact]
    public async Task Template_pin_round_trips_through_the_database()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode('a'), DateTimeOffset.UtcNow);
        invitation.PinTemplate("klasik-dugun", 3);

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Invitations.Add(invitation);
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateDbContext(connectionString);
        var reloaded = await readContext.Invitations.SingleAsync(i => i.Id == invitation.Id);

        Assert.Equal("klasik-dugun", reloaded.TemplateKey);
        Assert.Equal(3, reloaded.RendererVersion);
        Assert.Equal(1, reloaded.Revision);
    }

    [Fact]
    public async Task Invitation_pin_pairing_check_constraint_rejects_a_key_without_a_version()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        // Bypasses the domain guard clause on purpose: proves the DB itself, not just application
        // code, refuses a template_key without a paired renderer_version.
        command.CommandText = """
            INSERT INTO invitations (
                id, account_id, public_code, state, template_key, renderer_version, created_at, revision)
            VALUES ($1, $2, $3, 'Draft', 'klasik-dugun', NULL, now(), 0)
            """;
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(PublicCode('a'));

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("23514", exception.SqlState); // check_violation
    }

    [Fact]
    public async Task Second_working_content_for_the_same_invitation_is_rejected_by_the_database()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode('a'), DateTimeOffset.UtcNow);

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Invitations.Add(invitation);
            seedContext.WorkingContents.Add(WorkingContent.Create(
                Guid.NewGuid(), invitation.Id, 1, "{}", DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        // A brand-new DbContext instance (simulating a second, independent request/process with no
        // in-memory knowledge of the first insert) tries to add a second WorkingContent for the same
        // invitation_id. Only the DB unique index on WorkingContent.InvitationId can reject this.
        await using var secondRequestContext = CreateDbContext(connectionString);
        secondRequestContext.WorkingContents.Add(WorkingContent.Create(
            Guid.NewGuid(), invitation.Id, 1, "{}", DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => secondRequestContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgresException.SqlState); // unique_violation
    }

    [Fact]
    public async Task WorkingContent_autosave_round_trips_sparse_content_and_rejects_a_stale_concurrent_write()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), PublicCode('a'), DateTimeOffset.UtcNow);
        var workingContentId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Invitations.Add(invitation);
            seedContext.WorkingContents.Add(WorkingContent.Create(
                workingContentId, invitation.Id, 1, """{"eventType":"dugun"}""", DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        // Two independent "requests" load the same draft concurrently, both from revision 0.
        await using var firstWriterContext = CreateDbContext(connectionString);
        var firstWriterView = await firstWriterContext.WorkingContents.SingleAsync(c => c.Id == workingContentId);

        await using var secondWriterContext = CreateDbContext(connectionString);
        var secondWriterView = await secondWriterContext.WorkingContents.SingleAsync(c => c.Id == workingContentId);

        // The first writer's autosave lands and is a genuine round trip of sparse/partial content.
        firstWriterView.ReplaceContent("""{"eventType":"dugun","hostNames":"Ada & Grace"}""", 1, DateTimeOffset.UtcNow);
        await firstWriterContext.SaveChangesAsync();

        await using (var verifyContext = CreateDbContext(connectionString))
        {
            var persisted = await verifyContext.WorkingContents.SingleAsync(c => c.Id == workingContentId);

            // jsonb re-serializes on write (e.g. drops insignificant whitespace), so compare parsed
            // structure rather than the raw literal - this is still a genuine content round trip.
            using var persistedJson = JsonDocument.Parse(persisted.Content);
            Assert.Equal("dugun", persistedJson.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("Ada & Grace", persistedJson.RootElement.GetProperty("hostNames").GetString());
            Assert.Equal(1, persisted.Revision);
        }

        // The second writer still holds the stale (pre-autosave) revision and now tries to save on
        // top of it - the explicit revision/concurrency token (not application code) must reject this.
        secondWriterView.ReplaceContent("""{"eventType":"baby-shower"}""", 1, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondWriterContext.SaveChangesAsync());
    }

    [Fact]
    public async Task TemplateDefinition_key_uniqueness_is_enforced_by_the_database()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.TemplateDefinitions.Add(TemplateDefinition.Create(
                Guid.NewGuid(), "klasik-dugun", "Klasik Düğün", "dugun",
                isActive: true, isPremium: false, currentRendererVersion: 1,
                previewImageUrl: null, supportedModules: "[]", requiredFields: "[]", recommendedFields: "[]"));
            await seedContext.SaveChangesAsync();
        }

        await using var secondRequestContext = CreateDbContext(connectionString);
        secondRequestContext.TemplateDefinitions.Add(TemplateDefinition.Create(
            Guid.NewGuid(), "klasik-dugun", "Klasik Düğün (kopya)", "dugun",
            isActive: true, isPremium: false, currentRendererVersion: 1,
            previewImageUrl: null, supportedModules: "[]", requiredFields: "[]", recommendedFields: "[]"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => secondRequestContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgresException.SqlState); // unique_violation
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;

        return new DavetiyeDbContext(options);
    }

    private static string PublicCode(char value) =>
        new(value, PublicInvitationCode.EncodedLength);

    private static async Task RunMigratorAsync(string connectionString, string? target = null)
    {
        var migratorAssembly = FindMigratorAssembly();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(migratorAssembly);
        if (target is not null)
        {
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(target);
        }

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
}
