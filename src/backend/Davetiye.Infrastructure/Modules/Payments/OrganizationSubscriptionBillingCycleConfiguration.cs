using Davetiye.Domain.Modules.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Payments;

internal sealed class OrganizationSubscriptionBillingCycleConfiguration : IEntityTypeConfiguration<OrganizationSubscriptionBillingCycle>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscriptionBillingCycle> builder)
    {
        builder.HasKey(cycle => cycle.Id);

        builder.Property(cycle => cycle.ProviderCycleId)
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(cycle => cycle.Status)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(cycle => new { cycle.OrganizationSubscriptionId, cycle.ProviderCycleId })
            .HasDatabaseName("ux_organization_subscription_cycles_provider_identity")
            .IsUnique();
        builder.HasIndex(cycle => new { cycle.OrganizationSubscriptionId, cycle.PeriodStartsAtUtc })
            .HasDatabaseName("ix_organization_subscription_cycles_subscription_period_start");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_provider_identity_nonblank",
                "length(btrim(provider_cycle_id)) > 0");
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_period_order",
                "period_ends_at_utc > period_starts_at_utc");
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_status_supported",
                "status IN ('Pending', 'Failed', 'Succeeded')");
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_failure_notice_pairing",
                "(first_failure_at_utc IS NULL) = (failure_notice_recorded_at_utc IS NULL)");
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_status_timestamps",
                "(status = 'Pending' AND first_failure_at_utc IS NULL AND succeeded_at_utc IS NULL) OR " +
                "(status = 'Failed' AND first_failure_at_utc IS NOT NULL AND succeeded_at_utc IS NULL) OR " +
                "(status = 'Succeeded' AND succeeded_at_utc IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_organization_subscription_cycles_success_notice_requires_success",
                "success_notice_recorded_at_utc IS NULL OR succeeded_at_utc IS NOT NULL");
        });
    }
}
