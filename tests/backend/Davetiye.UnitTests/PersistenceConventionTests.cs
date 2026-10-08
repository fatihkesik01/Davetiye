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
                "20260928170000_M5B_IntegrationFoundationInboxOutbox",
                "20260929200313_P2M2_InvitationsAndTemplates",
                "20261001120058_P3M2_EntitlementsAndGrantReservations",
                "20261001211718_P3M3_PublicationSnapshotAndWindow",
                "20261002125108_P3M5_InvitationDeletionOverlay",
                "20261002193444_P3M7_AggregateInvitationViews",
                "20261002224848_P4M1_CreatorMediaContract",
                "20261004141823_P4M2_CreatorMediaIntentReservation",
                "20261004155857_P4M4_MediaVerification",
                "20261004193329_P4M5PublishedMediaSnapshot",
                "20261004230832_P5M1_RsvpSchema",
                "20261005120754_P6M1_MemorySchema",
                "20261005183806_P7M1_GiftRegistrySchema",
                "20261005214945_P8M2PaymentAttempts",
                "20261005234432_P8M4PaymentAttemptSettlementIdentity",
                "20261006064820_P8M4PaymentAttemptReversalState",
                "20261006065915_P8M4ChargebackResolution",
                "20261006073739_P8M5OrganizationSubscriptions",
                "20261006081856_P8M5OrganizationSubscriptionExpiryReminder",
                "20261006101308_P9M2AdminBanAuditSchema",
                "20261006104138_P9M5PaymentAttemptUpdatedAtPagingIndex",
                "20261006115815_P9M4PlanTemplateDescriptions",
                "20261006123301_P9M4PlanBillingKindSnapshot",
                "20261006124446_P9M4PaymentAttemptBillingKindSnapshot",
                "20261006132634_P9M4OrganizationSubscriptionPriceSnapshot",
                "20261006192152_P10M4AccountConsentRecords",
                "20261006195142_P10M3AccountDeletionLifecycle",
                "20261006201318_P10M3EmailDispatchLinearization",
                "20261008130723_P11UiPreferences",
                "20261008153057_P11AvatarPreference",
                "20261008191425_P11DefaultAppearanceLight",
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
        Assert.Contains(
            "CREATE INDEX ix_payment_attempts_updated_at_id ON payment_attempts (updated_at DESC, id DESC)",
            script,
            StringComparison.Ordinal);
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
