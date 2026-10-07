using System.Diagnostics;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class GiftRegistryPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Gift_graph_round_trips_with_contact_privacy_same_invitation_integrity_and_restrictive_deletes()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitationId = Guid.NewGuid(); // Gift owns ID references; no Invitations row is required.
        var foreignInvitationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var item = GiftItem.Create(Guid.NewGuid(), invitationId, "Tea set", 4, 0, now);
        var session = GuestGiftSession.Create(Guid.NewGuid(), invitationId, GuestGiftSession.RequiredPurpose,
            1, CreateDigest(19), now);
        var reservation = GiftReservation.Create(Guid.NewGuid(), invitationId, item.Id, session.Id, 2,
            "Ada Lovelace", "ada@example.test", "+90 555 000 0000", now);

        await using (var seed = CreateDbContext(connectionString))
        {
            seed.GiftItems.Add(item);
            seed.GuestGiftSessions.Add(session);
            seed.GiftReservations.Add(reservation);
            await seed.SaveChangesAsync();
        }

        await using (var read = CreateDbContext(connectionString))
        {
            var savedItem = await read.GiftItems.SingleAsync();
            var savedSession = await read.GuestGiftSessions.SingleAsync();
            var savedReservation = await read.GiftReservations.SingleAsync();
            Assert.Equal(invitationId, savedItem.InvitationId);
            Assert.Equal("Tea set", savedItem.Name);
            Assert.Equal(4, savedItem.RequestedQuantity);
            Assert.Equal(2, savedReservation.Quantity);
            Assert.Equal("Ada Lovelace", savedReservation.GuestFullName);
            Assert.Equal("ada@example.test", savedReservation.Email);
            Assert.Equal("+90 555 000 0000", savedReservation.Phone);
            Assert.Equal(GuestGiftSession.RequiredPurpose, savedSession.Purpose);
            Assert.Equal(GuestGiftSession.HmacSha256DigestLength, savedSession.HmacDigest.Length);
            Assert.Null(savedSession.RevokedAt); // No independent capability expiry is stored.
        }

        await using (var mismatch = CreateDbContext(connectionString))
        {
            var foreignSession = GuestGiftSession.Create(Guid.NewGuid(), foreignInvitationId,
                GuestGiftSession.RequiredPurpose, 1, CreateDigest(21), now);
            mismatch.GuestGiftSessions.Add(foreignSession);
            await mismatch.SaveChangesAsync();
            mismatch.GiftReservations.Add(GiftReservation.Create(Guid.NewGuid(), invitationId, item.Id,
                foreignSession.Id, 1, "Guest", null, null, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => mismatch.SaveChangesAsync());
        }

        await using (var itemDelete = CreateDbContext(connectionString))
        {
            itemDelete.GiftItems.Remove(await itemDelete.GiftItems.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => itemDelete.SaveChangesAsync());
        }

        await using (var sessionDelete = CreateDbContext(connectionString))
        {
            sessionDelete.GuestGiftSessions.Remove(await sessionDelete.GuestGiftSessions.SingleAsync(value => value.Id == session.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => sessionDelete.SaveChangesAsync());
        }

        // Cancellation/removal is a physical delete of the reservation, so its name and optional contacts go too.
        await using (var cancel = CreateDbContext(connectionString))
        {
            await cancel.GiftReservations.Where(value => value.Id == reservation.Id).ExecuteDeleteAsync();
        }

        await using (var verifyDelete = CreateDbContext(connectionString))
        {
            Assert.False(await verifyDelete.GiftReservations.AnyAsync());
            var savedSession = await verifyDelete.GuestGiftSessions.SingleAsync(value => value.Id == session.Id);
            savedSession.Revoke(now.AddMinutes(1));
            await verifyDelete.SaveChangesAsync();
            Assert.Equal(now.AddMinutes(1), savedSession.RevokedAt);
            await verifyDelete.GuestGiftSessions.Where(value => value.Id == session.Id).ExecuteDeleteAsync();
            await verifyDelete.GiftItems.ExecuteDeleteAsync();
            Assert.False(await verifyDelete.GuestGiftSessions.AnyAsync(value => value.Id == session.Id));
            Assert.False(await verifyDelete.GiftItems.AnyAsync());
        }
    }

    private static byte[] CreateDigest(byte firstByte)
    {
        var digest = new byte[GuestGiftSession.HmacSha256DigestLength];
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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) current = current.Parent;
        Assert.NotNull(current);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var migrator = Path.Combine(current!.FullName, "tools", "Davetiye.DatabaseMigrator", "bin", configuration,
            "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(migrator), $"Migrator assembly was not built: {migrator}");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(migrator);
        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the database migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0,
            $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }
}
