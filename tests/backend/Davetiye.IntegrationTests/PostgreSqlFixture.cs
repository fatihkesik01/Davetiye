using System.Security.Cryptography;
using System.Net;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Davetiye.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string LocalConnectionVariable = "DAVETIYE_TEST_POSTGRES_ADMIN_CONNECTION";
    private readonly PostgreSqlContainer? container;
    private readonly string? localAdminConnectionString;
    private readonly List<string> createdDatabases = [];

    public PostgreSqlFixture()
    {
        localAdminConnectionString = Environment.GetEnvironmentVariable(LocalConnectionVariable);
        if (string.IsNullOrWhiteSpace(localAdminConnectionString))
        {
            container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("davetiye_harness")
                .WithUsername("davetiye_harness")
                .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
                .Build();
        }
        else
        {
            var connection = new NpgsqlConnectionStringBuilder(localAdminConnectionString);
            if (connection.Database != "postgres" ||
                !(IPAddress.TryParse(connection.Host, out var address) && IPAddress.IsLoopback(address)) &&
                !string.Equals(connection.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{LocalConnectionVariable} must target the local 'postgres' database over a loopback host.");
            }
        }
    }

    public string AdminConnectionString => localAdminConnectionString ?? container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (container is not null)
        {
            await container.StartAsync();
            return;
        }

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            return;
        }

        var adminConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        foreach (var databaseName in createdDatabases)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"davetiye_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
        createdDatabases.Add(databaseName);

        return new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}
