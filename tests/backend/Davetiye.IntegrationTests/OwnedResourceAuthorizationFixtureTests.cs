using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

/// <summary>
/// M8's deliberately test-only BOLA/IDOR convention proof. This does not model an Invitation or
/// expose an API endpoint: future Creator-owned features must repeat this negative query pattern
/// against their own production store/query.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OwnedResourceAuthorizationFixtureTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Creator_cannot_read_a_foreign_owned_resource_by_its_internal_id()
    {
        await using var fixture = await TestOnlyOwnedResourceFixture.CreateAsync(postgreSql);

        var ownResource = await fixture.FindForOwnerAsync(fixture.CreatorA.AccountId, fixture.ResourceOwnedByAId);
        var foreignResource = await fixture.FindForOwnerAsync(fixture.CreatorB.AccountId, fixture.ResourceOwnedByAId);

        Assert.NotNull(ownResource);
        Assert.Null(foreignResource);
    }

    [Fact]
    public async Task Creator_cannot_mutate_a_foreign_owned_resource_by_its_internal_id()
    {
        await using var fixture = await TestOnlyOwnedResourceFixture.CreateAsync(postgreSql);

        var affectedRows = await fixture.UpdateForOwnerAsync(
            fixture.CreatorB.AccountId,
            fixture.ResourceOwnedByAId,
            "foreign mutation must not persist");

        Assert.Equal(0, affectedRows);
        Assert.Equal(
            "creator-a private value",
            await fixture.ReadValueAsync(fixture.ResourceOwnedByAId));
    }

    private sealed class TestOnlyOwnedResourceFixture : IAsyncDisposable
    {
        private readonly string connectionString;

        private TestOnlyOwnedResourceFixture(
            string connectionString,
            TestCreator creatorA,
            TestCreator creatorB,
            Guid resourceOwnedByAId)
        {
            this.connectionString = connectionString;
            CreatorA = creatorA;
            CreatorB = creatorB;
            ResourceOwnedByAId = resourceOwnedByAId;
        }

        public TestCreator CreatorA { get; }

        public TestCreator CreatorB { get; }

        public Guid ResourceOwnedByAId { get; }

        public static async Task<TestOnlyOwnedResourceFixture> CreateAsync(PostgreSqlFixture postgreSql)
        {
            var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
            var creatorA = TestCreator.Create();
            var creatorB = TestCreator.Create();
            var resourceOwnedByAId = Guid.NewGuid();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await ExecuteAsync(connection, """
                CREATE TABLE m8_test_creator_accounts (
                    identity_user_id uuid PRIMARY KEY,
                    account_id uuid NOT NULL UNIQUE
                );
                CREATE TABLE m8_test_owned_resources (
                    id uuid PRIMARY KEY,
                    owner_account_id uuid NOT NULL REFERENCES m8_test_creator_accounts(account_id),
                    private_value text NOT NULL
                );
                """);

            await InsertCreatorAsync(connection, creatorA);
            await InsertCreatorAsync(connection, creatorB);
            await using var resourceCommand = new NpgsqlCommand(
                "INSERT INTO m8_test_owned_resources (id, owner_account_id, private_value) VALUES ($1, $2, $3)",
                connection);
            resourceCommand.Parameters.AddWithValue(resourceOwnedByAId);
            resourceCommand.Parameters.AddWithValue(creatorA.AccountId);
            resourceCommand.Parameters.AddWithValue("creator-a private value");
            await resourceCommand.ExecuteNonQueryAsync();

            return new TestOnlyOwnedResourceFixture(connectionString, creatorA, creatorB, resourceOwnedByAId);
        }

        public async Task<string?> FindForOwnerAsync(Guid currentAccountId, Guid resourceId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT private_value FROM m8_test_owned_resources WHERE id = $1 AND owner_account_id = $2",
                connection);
            command.Parameters.AddWithValue(resourceId);
            command.Parameters.AddWithValue(currentAccountId);
            return (string?)await command.ExecuteScalarAsync();
        }

        public async Task<int> UpdateForOwnerAsync(Guid currentAccountId, Guid resourceId, string value)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE m8_test_owned_resources SET private_value = $1 WHERE id = $2 AND owner_account_id = $3",
                connection);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(resourceId);
            command.Parameters.AddWithValue(currentAccountId);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<string> ReadValueAsync(Guid resourceId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT private_value FROM m8_test_owned_resources WHERE id = $1",
                connection);
            command.Parameters.AddWithValue(resourceId);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async Task InsertCreatorAsync(NpgsqlConnection connection, TestCreator creator)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO m8_test_creator_accounts (identity_user_id, account_id) VALUES ($1, $2)",
                connection);
            command.Parameters.AddWithValue(creator.IdentityUserId);
            command.Parameters.AddWithValue(creator.AccountId);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string commandText)
        {
            await using var command = new NpgsqlCommand(commandText, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record TestCreator(Guid IdentityUserId, Guid AccountId)
    {
        public static TestCreator Create() => new(Guid.NewGuid(), Guid.NewGuid());
    }
}
