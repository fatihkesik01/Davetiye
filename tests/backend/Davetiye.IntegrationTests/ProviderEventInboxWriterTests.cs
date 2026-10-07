using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderEventInboxWriterTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Event_is_acknowledged_as_duplicate_only_after_the_database_unique_index_confirms_it()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using (var migrate = CreateDbContext(connectionString))
            await migrate.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        await using (var firstRequest = CreateDbContext(connectionString))
        {
            var result = await CreateWriter(firstRequest).AppendAsync(
                "iyzico-hpp", "signed-event-sha256", "{\"status\":\"SUCCESS\"}", now, CancellationToken.None);
            Assert.Equal(ProviderEventInboxWriteOutcome.Accepted, result);
        }

        await using (var repeatedRequest = CreateDbContext(connectionString))
        {
            var result = await CreateWriter(repeatedRequest).AppendAsync(
                "iyzico-hpp", "signed-event-sha256", "{\"status\":\"SUCCESS\"}", now, CancellationToken.None);
            Assert.Equal(ProviderEventInboxWriteOutcome.Duplicate, result);
        }

        await using var verification = CreateDbContext(connectionString);
        Assert.Equal(1, await verification.InboxMessages.CountAsync());
    }

    private static ProviderEventInboxWriter CreateWriter(DavetiyeDbContext context) =>
        new(new InboxMessageRepository(context));

    private static DavetiyeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;
        return new DavetiyeDbContext(options);
    }
}
