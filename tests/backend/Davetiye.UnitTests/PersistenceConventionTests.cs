using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PersistenceConventionTests
{
    [Fact]
    public void PostgreSql_model_applies_foundation_conventions()
    {
        using var context = CreateContext<ConventionProbeDbContext>();
        var entity = context.Model.FindEntityType(typeof(ConventionProbe))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        Assert.Equal("probes", entity.GetTableName());
        Assert.Equal("id", entity.FindProperty(nameof(ConventionProbe.Id))!.GetColumnName(table));
        Assert.Equal("uuid", entity.FindProperty(nameof(ConventionProbe.Id))!.GetColumnType());
        Assert.Equal(
            "timestamp with time zone",
            entity.FindProperty(nameof(ConventionProbe.CreatedAt))!.GetColumnType());

        var amount = entity.FindProperty(nameof(ConventionProbe.TotalAmount))!;
        Assert.Equal(PersistenceConstants.MoneyPrecision, amount.GetPrecision());
        Assert.Equal(PersistenceConstants.MoneyScale, amount.GetScale());

        var currency = entity.FindProperty(nameof(ConventionProbe.Currency))!;
        Assert.Equal(PersistenceConstants.CurrencyCodeLength, currency.GetMaxLength());
        Assert.True(currency.IsFixedLength());

        Assert.True(entity.FindProperty(nameof(ConventionProbe.Revision))!.IsConcurrencyToken);
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
    }

    [Fact]
    public void DateTime_persistence_is_rejected_in_favor_of_UTC_DateTimeOffset()
    {
        using var context = CreateContext<InvalidInstantDbContext>();

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("DateTimeOffset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Foundation_migration_is_discoverable_from_the_single_migration_assembly()
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=migration_metadata;Username=runtime_test",
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;
        using var context = new DavetiyeDbContext(options);

        Assert.Equal(
            [
                "20260928153000_FoundationBaseline",
                "20260928160000_M5A_IdentityAccountsPlansSettings",
                "20260928170000_M5B_IntegrationFoundationInboxOutbox"
            ],
            context.Database.GetMigrations());
    }

    [Fact]
    public void Foundation_migration_generates_an_idempotent_PostgreSql_script()
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=migration_script;Username=runtime_test",
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;
        using var context = new DavetiyeDbContext(options);
        var migrator = context.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("__EFMigrationsHistory", script, StringComparison.Ordinal);
        Assert.Contains("20260928153000_FoundationBaseline", script, StringComparison.Ordinal);
    }

    private static TContext CreateContext<TContext>()
        where TContext : DavetiyeDbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=conventions;Username=runtime_test")
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    private sealed class ConventionProbeDbContext(DbContextOptions<ConventionProbeDbContext> options)
        : DavetiyeDbContext(options)
    {
        public DbSet<ConventionProbe> Probes => Set<ConventionProbe>();
    }

    private sealed class InvalidInstantDbContext(DbContextOptions<InvalidInstantDbContext> options)
        : DavetiyeDbContext(options)
    {
        public DbSet<InvalidInstantProbe> Probes => Set<InvalidInstantProbe>();
    }

    private sealed class ConventionProbe
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public decimal TotalAmount { get; init; }
        public string Currency { get; init; } = "TRY";
        public long Revision { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
        public DateTimeOffset? PurgeAfter { get; init; }
    }

    private sealed class InvalidInstantProbe
    {
        public Guid Id { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
