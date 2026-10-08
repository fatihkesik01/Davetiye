using System.Diagnostics;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class DatabaseFoundationTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task PostgreSql_container_accepts_real_connections()
    {
        await using var connection = new NpgsqlConnection(postgreSql.AdminConnectionString);

        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task Migrator_applies_the_foundation_to_an_empty_database_and_is_idempotent()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();

        await RunMigratorAsync(connectionString);
        await RunMigratorAsync(connectionString);

        await using var dbContext = CreateDbContext(connectionString);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var known = dbContext.Database.GetMigrations();

        Assert.Equal(known, applied);
        Assert.NotEmpty(applied);
    }

    [Fact]
    public async Task Migrator_supports_targeted_then_latest_upgrade_path()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var dbContext = CreateDbContext(connectionString);
        var firstMigration = dbContext.Database.GetMigrations().First();

        await RunMigratorAsync(connectionString, DatabaseMigrationRunner.InitialDatabaseTarget);
        await RunMigratorAsync(connectionString, firstMigration);
        await RunMigratorAsync(connectionString);

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        Assert.Equal(dbContext.Database.GetMigrations(), applied);
    }

    [Fact]
    public async Task P10M3_downgrade_is_refused_when_account_deletion_evidence_exists()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var identityUserId = Guid.NewGuid();
            dbContext.Users.Add(new ApplicationUser
            {
                Id = identityUserId,
                UserName = $"deletion-{identityUserId:N}",
                NormalizedUserName = $"DELETION-{identityUserId:N}",
                Email = $"{identityUserId:N}@example.test",
                NormalizedEmail = $"{identityUserId:N}@EXAMPLE.TEST"
            });
            var account = Account.Create(Guid.NewGuid(), identityUserId, AccountType.Individual,
                "Deletion evidence", DateTimeOffset.UtcNow);
            account.BeginDeletion(DateTimeOffset.UtcNow);
            dbContext.Accounts.Add(account);
            await dbContext.SaveChangesAsync();

            var migrations = dbContext.Database.GetMigrations().ToArray();
            var deletionMigration = Assert.Single(migrations, migration =>
                migration.EndsWith("_P10M3AccountDeletionLifecycle", StringComparison.Ordinal));
            var priorMigration = migrations.TakeWhile(migration => migration != deletionMigration).Last();
            var migrator = dbContext.GetService<IMigrator>();

            var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(priorMigration));
            Assert.Contains("cannot be downgraded while account-deletion or settlement evidence exists", exception.Message);
            // Every later migration contains no deletion evidence and rolls back first, newest to oldest;
            // the lifecycle migration then refuses to erase deletion evidence, so everything up to and
            // including it stays applied (independent of how many migrations follow it).
            Assert.Equal(migrations[..(Array.IndexOf(migrations, deletionMigration) + 1)],
                await dbContext.Database.GetAppliedMigrationsAsync());
        }
    }

    [Fact]
    public async Task P10M3_allows_only_one_pending_deletion_token_per_account_and_preserves_history()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
        var accountId = Guid.NewGuid();
        var identityUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Users.Add(new ApplicationUser
            {
                Id = identityUserId,
                UserName = $"deletion-token-{identityUserId:N}",
                NormalizedUserName = $"DELETION-TOKEN-{identityUserId:N}",
                Email = $"{identityUserId:N}@example.test",
                NormalizedEmail = $"{identityUserId:N}@EXAMPLE.TEST"
            });
            seedContext.Accounts.Add(Account.Create(accountId, identityUserId, AccountType.Individual, "Token test", now));
            seedContext.AccountDeletionRequests.Add(AccountDeletionRequest.Create(Guid.NewGuid(), accountId,
                new string('a', 64), now, now.AddHours(1)));
            await seedContext.SaveChangesAsync();
        }

        await using (var competingContext = CreateDbContext(connectionString))
        {
            competingContext.AccountDeletionRequests.Add(AccountDeletionRequest.Create(Guid.NewGuid(), accountId,
                new string('b', 64), now.AddMinutes(1), now.AddHours(1)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => competingContext.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using (var replacementContext = CreateDbContext(connectionString))
        {
            var prior = await replacementContext.AccountDeletionRequests.SingleAsync();
            Assert.True(prior.Supersede(now.AddMinutes(2)));
            replacementContext.AccountDeletionRequests.Add(AccountDeletionRequest.Create(Guid.NewGuid(), accountId,
                new string('c', 64), now.AddMinutes(2), now.AddHours(1)));
            await replacementContext.SaveChangesAsync();
            Assert.Equal(2, await replacementContext.AccountDeletionRequests.CountAsync());
        }
    }

    [Fact]
    public async Task P10M3_dispatch_history_cannot_be_dropped_after_transport_authorization()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.OutboxMessages.Add(OutboxMessage.Create(messageId, "notifications.email", "{}", now,
                Guid.NewGuid()));
            await seedContext.SaveChangesAsync();
            await Assert.ThrowsAsync<PostgresException>(() => seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE outbox_messages SET dispatch_started_at_utc = created_at - interval '1 second' WHERE id = {messageId}"));
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE outbox_messages SET dispatch_started_at_utc = created_at + interval '1 second' WHERE id = {messageId}");
            await Assert.ThrowsAsync<PostgresException>(() => seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE outbox_messages SET processed_at = created_at WHERE id = {messageId}"));

            var migrations = seedContext.Database.GetMigrations().ToArray();
            var dispatchMigration = Assert.Single(migrations, migration =>
                migration.EndsWith("_P10M3EmailDispatchLinearization", StringComparison.Ordinal));
            var priorMigration = migrations.TakeWhile(migration => migration != dispatchMigration).Last();
            var migrator = seedContext.GetService<IMigrator>();

            var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(priorMigration));
            Assert.Contains("dispatch history cannot be downgraded after external delivery was authorized", exception.Message);
            // Migrations after the dispatch migration roll back first; the dispatch migration then refuses,
            // so everything up to and including it stays applied.
            Assert.Equal(migrations[..(Array.IndexOf(migrations, dispatchMigration) + 1)],
                await seedContext.Database.GetAppliedMigrationsAsync());
        }
    }

    [Fact]
    public async Task M5A_migration_applies_to_an_empty_database_creates_the_expected_tables_and_is_idempotent()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();

        await RunMigratorAsync(connectionString);
        await RunMigratorAsync(connectionString);

        await using var dbContext = CreateDbContext(connectionString);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains("20260928160000_M5A_IdentityAccountsPlansSettings", applied);

        string[] expectedTables =
        [
            "asp_net_users",
            "asp_net_user_claims",
            "asp_net_user_logins",
            "asp_net_user_tokens",
            "accounts",
            "ban_records",
            "plans",
            "plan_entitlements",
            "account_plan_grants",
            "system_settings"
        ];

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
        await using var reader = await command.ExecuteReaderAsync();
        var actualTables = new List<string>();
        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        foreach (var expectedTable in expectedTables)
        {
            Assert.Contains(expectedTable, actualTables);
        }
    }

    [Fact]
    public async Task M5B_migration_applies_to_an_empty_database_creates_the_expected_tables_and_is_idempotent()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();

        await RunMigratorAsync(connectionString);
        await RunMigratorAsync(connectionString);

        await using var dbContext = CreateDbContext(connectionString);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains("20260928170000_M5B_IntegrationFoundationInboxOutbox", applied);

        string[] expectedTables = ["inbox_messages", "outbox_messages"];

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
        await using var reader = await command.ExecuteReaderAsync();
        var actualTables = new List<string>();
        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        foreach (var expectedTable in expectedTables)
        {
            Assert.Contains(expectedTable, actualTables);
        }
    }

    [Fact]
    public async Task P2M2_migration_applies_to_an_empty_database_creates_the_expected_tables_and_is_idempotent()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();

        await RunMigratorAsync(connectionString);
        await RunMigratorAsync(connectionString);

        await using var dbContext = CreateDbContext(connectionString);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains("20260929200313_P2M2_InvitationsAndTemplates", applied);

        string[] expectedTables = ["invitations", "working_contents", "template_definitions"];

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
        await using var reader = await command.ExecuteReaderAsync();
        var actualTables = new List<string>();
        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        foreach (var expectedTable in expectedTables)
        {
            Assert.Contains(expectedTable, actualTables);
        }
    }

    [Fact]
    public async Task Duplicate_account_for_the_same_identity_user_is_rejected_by_the_database()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var identityUserId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.Users.Add(new ApplicationUser
            {
                Id = identityUserId,
                UserName = $"user-{identityUserId:N}",
                NormalizedUserName = $"USER-{identityUserId:N}",
                Email = $"{identityUserId:N}@example.test",
                NormalizedEmail = $"{identityUserId:N}@EXAMPLE.TEST"
            });
            seedContext.Accounts.Add(Account.Create(
                Guid.NewGuid(),
                identityUserId,
                AccountType.Individual,
                "First Account",
                DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        // Application code never checks uniqueness itself (Account.Create has no such guard).
        // A brand-new DbContext instance (simulating a second, independent request/process with no
        // in-memory knowledge of the first Account) tries to insert a second Account row for the
        // same identity_user_id. Only the DB unique index configured in AccountConfiguration can
        // reject this - not EF's change tracker, which has no state to object with here.
        await using var secondRequestContext = CreateDbContext(connectionString);
        secondRequestContext.Accounts.Add(Account.Create(
            Guid.NewGuid(),
            identityUserId,
            AccountType.Individual,
            "Second Account",
            DateTimeOffset.UtcNow));

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
