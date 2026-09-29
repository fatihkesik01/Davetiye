using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Davetiye.Infrastructure.Persistence;

public sealed class DavetiyeDbContextFactory : IDesignTimeDbContextFactory<DavetiyeDbContext>
{
    public DavetiyeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set Database__ConnectionString for design-time migration commands.");
        }

        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;

        return new DavetiyeDbContext(options);
    }
}
