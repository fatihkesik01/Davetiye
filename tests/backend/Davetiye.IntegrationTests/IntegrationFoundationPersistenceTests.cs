using System.Diagnostics;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// Phase 1 task 9's "unique/idempotent claim ve retry-safe worker testi" acceptance criterion,
/// proven against a real PostgreSQL instance (Testcontainers), not a fake/in-memory provider:
///
/// (a) inserting the same (ProviderName, ProviderEventId) pair twice is genuinely rejected by
///     Postgres itself (23505 unique_violation), mirroring M5A's "one Account per Identity user"
///     proof pattern (two independent DbContext instances);
/// (b) <see cref="InboxMessageRepository.ClaimBatchAsync"/> is safe under real concurrent callers:
///     multiple genuinely concurrent tasks, each with its own DbContext/connection, claiming from a
///     shared pool of unprocessed rows, never receive the same row and never lose a row;
/// (c) <see cref="BatchMessageWorker{TMessage}"/> wired to the real repository correctly persists
///     "processed" on handler success and "rescheduled" on handler failure.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class IntegrationFoundationPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Duplicate_provider_event_for_the_same_provider_is_rejected_by_the_database()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var receivedAt = DateTimeOffset.UtcNow;

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.InboxMessages.Add(InboxMessage.Create(
                Guid.NewGuid(), "iyzico", "evt-duplicate", "{}", receivedAt));
            await seedContext.SaveChangesAsync();
        }

        // Application code never checks uniqueness itself (InboxMessage.Create has no such guard,
        // and InboxMessageRepository.AppendAsync performs a plain insert). A brand-new DbContext
        // instance (simulating a second, independent request/process with no in-memory knowledge of
        // the first insert) tries to append a second inbox message for the same
        // (provider_name, provider_event_id) pair. Only the DB unique index configured in
        // InboxMessageConfiguration can reject this - not EF's change tracker, which has no state to
        // object with here.
        await using var secondRequestContext = CreateDbContext(connectionString);
        var secondRepository = new InboxMessageRepository(secondRequestContext);
        var duplicate = InboxMessage.Create(
            Guid.NewGuid(), "iyzico", "evt-duplicate", "{\"different\":true}", DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => secondRepository.AppendAsync(duplicate, CancellationToken.None));
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgresException.SqlState); // unique_violation
    }

    [Fact]
    public async Task ClaimBatchAsync_never_double_claims_or_loses_a_row_under_real_concurrency()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        const int totalRows = 30;
        const int workerCount = 6;
        const int batchSizePerWorker = 10; // 6 * 10 = 60 requested against only 30 available rows:
                                            // every worker genuinely competes for the same pool.
        var now = DateTimeOffset.UtcNow;

        await using (var seedContext = CreateDbContext(connectionString))
        {
            for (var i = 0; i < totalRows; i++)
            {
                seedContext.InboxMessages.Add(InboxMessage.Create(
                    Guid.NewGuid(), "iyzico", $"evt-{i}", "{}", now));
            }

            await seedContext.SaveChangesAsync();
        }

        // Each worker gets its OWN DbContext/connection, and all workers are launched together with
        // Task.WhenAll so their claim statements genuinely race against PostgreSQL concurrently -
        // this is not sequential calls dressed up as "concurrent".
        var claimTasks = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            await using var workerContext = CreateDbContext(connectionString);
            var repository = new InboxMessageRepository(workerContext);
            var claimed = await repository.ClaimBatchAsync(
                batchSizePerWorker, TimeSpan.FromMinutes(1), now, CancellationToken.None);
            return claimed.Select(message => message.Id).ToArray();
        });

        var claimedIdsPerWorker = await Task.WhenAll(claimTasks);
        var allClaimedIds = claimedIdsPerWorker.SelectMany(ids => ids).ToArray();

        // No double-claim: if any row had been handed to two workers, the flattened list would be
        // longer than the distinct set of ids it contains.
        Assert.Equal(allClaimedIds.Length, allClaimedIds.Distinct().Count());

        // No lost row: every seeded row was claimed by exactly one worker.
        Assert.Equal(totalRows, allClaimedIds.Length);
    }

    [Fact]
    public async Task Worker_marks_success_processed_and_reschedules_failure_against_real_postgres()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        // PostgreSQL's "timestamp with time zone" only has microsecond precision, while .NET's
        // DateTimeOffset ticks are 100ns; truncate so the round-tripped value compares equal.
        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var succeedingId = Guid.NewGuid();
        var failingId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.InboxMessages.Add(InboxMessage.Create(succeedingId, "iyzico", "evt-ok", "{}", now));
            seedContext.InboxMessages.Add(InboxMessage.Create(failingId, "iyzico", "evt-fail", "{}", now));
            await seedContext.SaveChangesAsync();
        }

        var expectedNextAttempt = now.AddMinutes(5);

        await using (var workerContext = CreateDbContext(connectionString))
        {
            var repository = new InboxMessageRepository(workerContext);
            var worker = new BatchMessageWorker<InboxMessage>(repository);

            // Test-only fake handler: production code never wires a fake handler into this worker.
            var result = await worker.ProcessBatchAsync(
                batchSize: 10,
                leaseDuration: TimeSpan.FromMinutes(1),
                now: now,
                handleAsync: (message, _) => Task.FromResult(message.Id == succeedingId),
                computeNextAttemptAt: _ => expectedNextAttempt);

            Assert.Equal(2, result.ClaimedCount);
            Assert.Equal(1, result.SucceededCount);
            Assert.Equal(1, result.RescheduledCount);
        }

        await using var verificationContext = CreateDbContext(connectionString);
        var succeeded = await verificationContext.InboxMessages.SingleAsync(m => m.Id == succeedingId);
        var failed = await verificationContext.InboxMessages.SingleAsync(m => m.Id == failingId);

        Assert.NotNull(succeeded.ProcessedAt);
        Assert.Null(succeeded.ClaimedUntil);

        Assert.Null(failed.ProcessedAt);
        Assert.Null(failed.ClaimedUntil);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal(expectedNextAttempt, failed.NextAttemptAt);
        Assert.False(failed.FailedPermanently);
    }

    [Fact]
    public async Task Outbox_append_claim_and_mark_processed_round_trip_against_real_postgres()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await using (var appendContext = CreateDbContext(connectionString))
        {
            var repository = new OutboxMessageRepository(appendContext);
            await repository.AppendAsync(
                OutboxMessage.Create(messageId, "email.rsvp-confirmation", "{}", now),
                CancellationToken.None);
        }

        await using (var workerContext = CreateDbContext(connectionString))
        {
            var repository = new OutboxMessageRepository(workerContext);
            var claimed = await repository.ClaimBatchAsync(
                10, TimeSpan.FromMinutes(1), now, CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(messageId, claimed[0].Id);
            Assert.NotNull(claimed[0].ClaimedUntil);

            await repository.MarkProcessedAsync(claimed[0], now, CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext(connectionString);
        var processed = await verificationContext.OutboxMessages.SingleAsync(m => m.Id == messageId);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Null(processed.ClaimedUntil);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

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
