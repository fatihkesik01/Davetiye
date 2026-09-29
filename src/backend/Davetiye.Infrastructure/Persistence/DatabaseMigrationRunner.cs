using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Persistence;

public sealed class DatabaseMigrationRunner(DavetiyeDbContext dbContext)
{
    public const string InitialDatabaseTarget = "0";

    public Task MigrateAsync(string? targetMigration, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(targetMigration)
            ? dbContext.Database.MigrateAsync(cancellationToken)
            : dbContext.Database.MigrateAsync(targetMigration, cancellationToken);
}
