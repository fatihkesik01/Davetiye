using Microsoft.Extensions.Options;
using Npgsql;

namespace Davetiye.Infrastructure.Persistence;

internal sealed class DatabaseOptionsValidator(bool isProduction)
    : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Database:ConnectionString is required.");
        }
        else
        {
            ValidateConnectionString(options.ConnectionString, failures);
        }

        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            failures.Add("Database:CommandTimeoutSeconds must be between 1 and 300.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateConnectionString(string connectionString, ICollection<string> failures)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                failures.Add("Database connection host is required.");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                failures.Add("Database name is required.");
            }

            if (string.IsNullOrWhiteSpace(builder.Username))
            {
                failures.Add("Database username is required.");
            }

            if (isProduction && string.IsNullOrWhiteSpace(builder.Password))
            {
                failures.Add("Database credentials are required in Production.");
            }
        }
        catch (ArgumentException)
        {
            failures.Add("Database:ConnectionString is not a valid PostgreSQL connection string.");
        }
    }
}
