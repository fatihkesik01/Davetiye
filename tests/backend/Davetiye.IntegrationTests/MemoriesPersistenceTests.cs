using System.Diagnostics;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Infrastructure.Modules.Memories;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class MemoriesPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Memory_graph_round_trips_and_purge_is_atomic_without_cross_module_foreign_keys()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitationId = Guid.NewGuid(); // Deliberately no Invitations row: Memories owns this ID reference only.
        var otherInvitationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var configuration = MemoryConfiguration.Create(Guid.NewGuid(), invitationId, now);
        Assert.False(configuration.IsEnabled);
        Assert.Equal(MemoryVisibility.CreatorOnly, configuration.Visibility);
        var otherConfiguration = MemoryConfiguration.Create(Guid.NewGuid(), otherInvitationId, now);

        var memory = Memory.Create(Guid.NewGuid(), invitationId, " Ayse ", "Hello", "🎉", true, now);
        var assetId = Guid.NewGuid(); // Deliberately no media_assets row.
        memory.AttachMedia(assetId, Guid.NewGuid());
        memory.Finalize(now.AddSeconds(1));
        var capability = MemoryUploadCapability.Create(Guid.NewGuid(), memory.Id, MemoryUploadCapability.RequiredPurpose,
            2, CreateDigest(1), now, now.AddMinutes(10));
        var textOnly = Memory.Create(Guid.NewGuid(), invitationId, null, "Only text", null, false, now);
        var otherMemory = Memory.Create(Guid.NewGuid(), otherInvitationId, null, "Other", null, false, now);

        await using (var seed = CreateDbContext(connectionString))
        {
            seed.MemoryConfigurations.AddRange(configuration, otherConfiguration);
            seed.Memories.AddRange(memory, textOnly, otherMemory);
            seed.MemoryUploadCapabilities.Add(capability);
            await seed.SaveChangesAsync();
        }

        await using (var verify = CreateDbContext(connectionString))
        {
            var loaded = await verify.Memories.Include(item => item.Media).SingleAsync(item => item.Id == memory.Id);
            Assert.Equal("Ayse", loaded.DisplayName);
            Assert.Equal(MemoryState.Published, loaded.State);
            Assert.Equal(assetId, Assert.Single(loaded.Media).MediaAssetId);
            Assert.Equal(MemoryUploadCapability.HmacSha256DigestLength,
                (await verify.MemoryUploadCapabilities.SingleAsync()).HmacDigest.Length);
        }

        await using (var rollback = CreateDbContext(connectionString))
        {
            await using var transaction = await rollback.Database.BeginTransactionAsync();
            await new MemoriesPurgeCoordinator(rollback).PurgeForInvitationAsync(invitationId, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        await using (var afterRollback = CreateDbContext(connectionString))
        {
            Assert.Equal(2, await afterRollback.Memories.CountAsync(item => item.InvitationId == invitationId));
            Assert.True(await afterRollback.MemoryMedia.AnyAsync());
            Assert.True(await afterRollback.MemoryUploadCapabilities.AnyAsync());
            Assert.True(await afterRollback.MemoryConfigurations.AnyAsync(item => item.InvitationId == invitationId));
        }

        await using (var withoutTransaction = CreateDbContext(connectionString))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new MemoriesPurgeCoordinator(withoutTransaction).PurgeForInvitationAsync(invitationId, CancellationToken.None));
        }

        await using (var purge = CreateDbContext(connectionString))
        {
            await using var transaction = await purge.Database.BeginTransactionAsync();
            await new MemoriesPurgeCoordinator(purge).PurgeForInvitationAsync(invitationId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        await using var afterPurge = CreateDbContext(connectionString);
        Assert.False(await afterPurge.Memories.AnyAsync(item => item.InvitationId == invitationId));
        Assert.False(await afterPurge.MemoryConfigurations.AnyAsync(item => item.InvitationId == invitationId));
        Assert.False(await afterPurge.MemoryMedia.AnyAsync());
        Assert.False(await afterPurge.MemoryUploadCapabilities.AnyAsync());
        // Another invitation's rows are untouched.
        Assert.True(await afterPurge.Memories.AnyAsync(item => item.Id == otherMemory.Id));
        Assert.True(await afterPurge.MemoryConfigurations.AnyAsync(item => item.InvitationId == otherInvitationId));
    }

    [Fact]
    public async Task Memory_constraints_enforce_lengths_state_shape_media_scope_and_single_active_capability()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitationId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
            INSERT INTO memory_configurations (id, invitation_id, is_enabled, visibility, created_at, updated_at, revision)
            VALUES (@id, @invitation_id, FALSE, 'CreatorOnly', now(), now(), 0);
            INSERT INTO memories (id, invitation_id, text, state, created_at, finalized_at, revision)
            VALUES (@memory_id, @invitation_id, 'hello', 'Published', now(), now(), 0);
            """, ("id", Guid.NewGuid()), ("invitation_id", invitationId), ("memory_id", memoryId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO memory_configurations (id, invitation_id, is_enabled, visibility, created_at, updated_at, revision)
            VALUES (@id, @invitation_id, FALSE, 'CreatorOnly', now(), now(), 0)
            """, "23505", ("id", Guid.NewGuid()), ("invitation_id", invitationId));
        await AssertSqlStateAsync(connection, """
            INSERT INTO memory_configurations (id, invitation_id, is_enabled, visibility, created_at, updated_at, revision)
            VALUES (@id, @invitation_id, FALSE, 'Everyone', now(), now(), 0)
            """, "23514", ("id", Guid.NewGuid()), ("invitation_id", Guid.NewGuid()));

        const string insertMemory = """
            INSERT INTO memories (id, invitation_id, display_name, text, emoji, state, created_at, finalized_at, hidden_at, revision)
            VALUES (@id, @invitation_id, @name, @text, @emoji, @state, now(), @finalized, @hidden, 0)
            """;
        (string, object)[] Args(string? name, string? text, string? emoji, string state, bool finalized, bool hidden) =>
        [
            ("id", Guid.NewGuid()), ("invitation_id", invitationId),
            ("name", (object?)name ?? DBNull.Value), ("text", (object?)text ?? DBNull.Value),
            ("emoji", (object?)emoji ?? DBNull.Value), ("state", state),
            ("finalized", finalized ? DateTimeOffset.UtcNow.AddMinutes(1) : DBNull.Value),
            ("hidden", hidden ? DateTimeOffset.UtcNow.AddMinutes(2) : DBNull.Value)
        ];
        // Over-length values hit the varchar(n) column guard (22001) before the check constraints.
        await AssertSqlStateAsync(connection, insertMemory, "22001", Args(new string('n', 61), "t", null, "Published", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "22001", Args(null, new string('t', 501), null, "Published", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "   ", null, "Published", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "22001", Args(null, "t", new string('e', 33), "Published", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "t", null, "Visible", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "t", null, "Published", false, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "t", null, "PendingMedia", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "t", null, "Hidden", true, false));
        await AssertSqlStateAsync(connection, insertMemory, "23514", Args(null, "t", null, "Published", true, true));
        await ExecuteAsync(connection, insertMemory, Args(null, new string('t', 500), null, "Hidden", true, true));

        // Media: ordinal ceiling, unique asset, unique ordinal per memory.
        const string insertMedia = "INSERT INTO memory_media (id, memory_id, media_asset_id, ordinal) VALUES (@id, @memory_id, @asset, @ordinal)";
        var assetId = Guid.NewGuid();
        await ExecuteAsync(connection, insertMedia, ("id", Guid.NewGuid()), ("memory_id", memoryId), ("asset", assetId), ("ordinal", 0));
        await AssertSqlStateAsync(connection, insertMedia, "23505", ("id", Guid.NewGuid()), ("memory_id", Guid.NewGuid()), ("asset", assetId), ("ordinal", 0));
        await AssertSqlStateAsync(connection, insertMedia, "23503", ("id", Guid.NewGuid()), ("memory_id", Guid.NewGuid()), ("asset", Guid.NewGuid()), ("ordinal", 0));
        await AssertSqlStateAsync(connection, insertMedia, "23505", ("id", Guid.NewGuid()), ("memory_id", memoryId), ("asset", Guid.NewGuid()), ("ordinal", 0));
        await AssertSqlStateAsync(connection, insertMedia, "23514", ("id", Guid.NewGuid()), ("memory_id", memoryId), ("asset", Guid.NewGuid()), ("ordinal", 3));

        // Capabilities: purpose, digest length, <=15 minute expiry, one active per memory.
        const string insertCapability = """
            INSERT INTO memory_upload_capabilities (id, memory_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at, consumed_at, revoked_at)
            VALUES (@id, @memory_id, @purpose, 1, @digest, now(), now() + @lifetime, @consumed, @revoked)
            """;
        (string, object)[] Cap(string purpose, byte[] digest, TimeSpan lifetime, bool consumed = false, bool revoked = false) =>
        [
            ("id", Guid.NewGuid()), ("memory_id", memoryId), ("purpose", purpose), ("digest", digest),
            ("lifetime", lifetime), ("consumed", consumed ? DateTimeOffset.UtcNow.AddMinutes(1) : DBNull.Value),
            ("revoked", revoked ? DateTimeOffset.UtcNow.AddMinutes(1) : DBNull.Value)
        ];
        await AssertSqlStateAsync(connection, insertCapability, "23514", Cap("rsvp-manage", CreateDigest(1), TimeSpan.FromMinutes(5)));
        await AssertSqlStateAsync(connection, insertCapability, "23514", Cap("memory-upload", new byte[31], TimeSpan.FromMinutes(5)));
        await AssertSqlStateAsync(connection, insertCapability, "23514", Cap("memory-upload", CreateDigest(1), TimeSpan.FromMinutes(16)));
        await AssertSqlStateAsync(connection, insertCapability, "23514", Cap("memory-upload", CreateDigest(1), TimeSpan.Zero));
        await ExecuteAsync(connection, insertCapability, Cap("memory-upload", CreateDigest(1), TimeSpan.FromMinutes(15)));
        await AssertSqlStateAsync(connection, insertCapability, "23505", Cap("memory-upload", CreateDigest(2), TimeSpan.FromMinutes(5)));
        await AssertSqlStateAsync(connection, insertCapability, "23505", Cap("memory-upload", CreateDigest(1), TimeSpan.FromMinutes(5), revoked: true));
        // Consumed/revoked capabilities do not occupy the single-active slot.
        await ExecuteAsync(connection, insertCapability, Cap("memory-upload", CreateDigest(3), TimeSpan.FromMinutes(5), consumed: true));
        await ExecuteAsync(connection, insertCapability, Cap("memory-upload", CreateDigest(4), TimeSpan.FromMinutes(5), revoked: true));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlStateAsync(NpgsqlConnection connection, string sql, string expectedState,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedState, exception.SqlState);
    }

    private static byte[] CreateDigest(byte firstByte)
    {
        var digest = new byte[32];
        digest[0] = firstByte;
        return digest;
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
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
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the database migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }

    private static string FindMigratorAssembly()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) current = current.Parent;
        Assert.NotNull(current);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var path = Path.Combine(current!.FullName, "tools", "Davetiye.DatabaseMigrator", "bin", configuration, "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(path), $"Migrator assembly was not built: {path}");
        return path;
    }
}
