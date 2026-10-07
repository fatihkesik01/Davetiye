using Davetiye.Domain.Modules.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Payments;

internal sealed class OrganizationSubscriptionConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscription> builder)
    {
        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.ProviderName)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(subscription => subscription.PriceAmountAtActivation)
            .HasPrecision(19, 4)
            .IsRequired();
        builder.Property(subscription => subscription.ProviderSubscriptionId)
            .HasMaxLength(128)
            .IsRequired();

        // Account and Plan are owned by other modules. Keep their identifiers scalar per ADR-0001.
        builder.HasIndex(subscription => new { subscription.ProviderName, subscription.ProviderSubscriptionId })
            .HasDatabaseName("ux_organization_subscriptions_provider_identity")
            .IsUnique();
        builder.HasIndex(subscription => new { subscription.AccountId, subscription.PaidThroughAtUtc })
            .HasDatabaseName("ix_organization_subscriptions_account_paid_through");
        builder.HasIndex(subscription => subscription.PaidThroughAtUtc)
            .HasDatabaseName("ix_organization_subscriptions_canceled_paid_through")
            .HasFilter("cancel_at_period_end = true AND access_expiry_reminder_queued_at_utc IS NULL");

        builder.HasMany(subscription => subscription.BillingCycles)
            .WithOne()
            .HasForeignKey(cycle => cycle.OrganizationSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.Navigation(subscription => subscription.BillingCycles)
            .HasField("_billingCycles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_organization_subscriptions_activation_price_positive", "price_amount_at_activation > 0");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_provider_identity_nonblank",
                "length(btrim(provider_name)) > 0 AND length(btrim(provider_subscription_id)) > 0");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_cancellation_pairing",
                "cancel_at_period_end = (cancel_requested_at_utc IS NOT NULL AND cancellation_boundary_at_utc IS NOT NULL) " +
                "AND ((cancel_requested_at_utc IS NULL) = (cancellation_boundary_at_utc IS NULL))");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_cancellation_boundary_not_after_paid_through",
                "cancellation_boundary_at_utc IS NULL OR cancellation_boundary_at_utc <= paid_through_at_utc");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_settlement_requires_cancellation",
                "NOT cancellation_settlement_applied OR cancel_at_period_end");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_renewal_cancellation_completion",
                "renewal_cancellation_completed_at_utc IS NULL OR " +
                "(renewal_cancellation_requested_at_utc IS NOT NULL AND " +
                "renewal_cancellation_completed_at_utc >= renewal_cancellation_requested_at_utc)");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_expiry_reminder_pairing",
                "access_expiry_reminder_queued_at_utc IS NULL OR " +
                "(cancel_at_period_end AND access_expiry_reminder_queued_at_utc < paid_through_at_utc)");
            table.HasCheckConstraint(
                "ck_organization_subscriptions_expiry_reminder_retry_pairing",
                "access_expiry_reminder_retry_after_utc IS NULL OR " +
                "(cancel_at_period_end AND access_expiry_reminder_queued_at_utc IS NULL " +
                "AND access_expiry_reminder_retry_after_utc < paid_through_at_utc)");
        });
    }
}
