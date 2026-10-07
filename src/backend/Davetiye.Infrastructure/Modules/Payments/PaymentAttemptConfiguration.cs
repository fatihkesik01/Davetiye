using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Payments;

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.PlanKey).HasMaxLength(64).IsRequired();
        builder.Property(attempt => attempt.BillingKindAtAttempt).HasConversion<string>().HasMaxLength(32).IsRequired();
        // Shared persistence money convention keeps commercial decimal precision consistent with Plan.PriceAmount.
        builder.Property(attempt => attempt.Amount).IsRequired();
        builder.Property(attempt => attempt.Currency).HasMaxLength(3).IsRequired();
        builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(attempt => attempt.Reference).HasMaxLength(64).IsRequired();
        builder.Property(attempt => attempt.Status).HasMaxLength(16).IsRequired();
        builder.Property(attempt => attempt.SettlementDisposition).HasConversion<string>().HasMaxLength(24);
        builder.Property(attempt => attempt.CheckoutUrl).HasMaxLength(2048);
        builder.Property(attempt => attempt.ProviderCheckoutId).HasMaxLength(200);
        builder.Property(attempt => attempt.ProviderPaymentId).HasMaxLength(32);
        builder.Property(attempt => attempt.ReversalKind).HasMaxLength(32);
        builder.Property(attempt => attempt.ChargebackResolution).HasMaxLength(16);

        builder.HasIndex(attempt => new { attempt.AccountId, attempt.InvitationId, attempt.IdempotencyKey })
            .HasDatabaseName("ux_payment_attempts_account_purchase_idempotency")
            .IsUnique();
        builder.HasIndex(attempt => new { attempt.AccountId, attempt.InvitationId })
            .HasDatabaseName("ux_payment_attempts_account_purchase_pending")
            .IsUnique()
            .HasFilter("status IN ('Pending', 'Unknown')");
        builder.HasIndex(attempt => attempt.Reference).IsUnique();
        builder.HasIndex(attempt => attempt.ProviderCheckoutId).IsUnique();
        builder.HasIndex(attempt => attempt.ProviderPaymentId)
            .HasDatabaseName("ux_payment_attempts_provider_payment_id")
            .IsUnique()
            .HasFilter("provider_payment_id IS NOT NULL");
        builder.HasIndex(attempt => attempt.GrantedPlanGrantId)
            .HasDatabaseName("ux_payment_attempts_granted_plan_grant_id")
            .IsUnique()
            .HasFilter("granted_plan_grant_id IS NOT NULL");
        builder.HasIndex(attempt => new { attempt.AccountId, attempt.CreatedAt });
        builder.HasIndex(attempt => new { attempt.UpdatedAt, attempt.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_payment_attempts_updated_at_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payment_attempts_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_payment_attempts_currency_try", "currency = 'TRY'");
            table.HasCheckConstraint("ck_payment_attempts_billing_kind_at_attempt", "billing_kind_at_attempt = 'OneTime'");
            table.HasCheckConstraint("ck_payment_attempts_status_supported",
                "status IN ('Pending', 'Unknown', 'Failed', 'Canceled', 'Succeeded', 'Reversed')");
            table.HasCheckConstraint("ck_payment_attempts_idempotency_key_length",
                "length(idempotency_key) BETWEEN 16 AND 100");
            table.HasCheckConstraint("ck_payment_attempts_success_identity_pairing",
                "(settlement_disposition IS NULL) = (provider_payment_id IS NULL)");
            table.HasCheckConstraint("ck_payment_attempts_settlement_disposition_supported",
                "settlement_disposition IS NULL OR settlement_disposition IN ('Granted', 'NoEntitlement')");
            table.HasCheckConstraint("ck_payment_attempts_grant_disposition_pairing",
                "(settlement_disposition IS NULL AND granted_plan_grant_id IS NULL) OR " +
                "(settlement_disposition = 'Granted' AND granted_plan_grant_id IS NOT NULL) OR " +
                "(settlement_disposition = 'NoEntitlement' AND granted_plan_grant_id IS NULL)");
            table.HasCheckConstraint("ck_payment_attempts_success_requires_provider_identity",
                "status NOT IN ('Succeeded', 'Reversed') OR (provider_payment_id IS NOT NULL AND settlement_disposition IS NOT NULL)");
            table.HasCheckConstraint("ck_payment_attempts_reversal_state_pairing",
                "(reversed_at_utc IS NULL) = (reversal_kind IS NULL)");
            table.HasCheckConstraint("ck_payment_attempts_reversal_state_supported",
                "(status = 'Reversed' AND reversed_at_utc IS NOT NULL AND reversal_kind IN ('FullRefund', 'FinalLostChargeback')) OR (status <> 'Reversed' AND reversed_at_utc IS NULL AND reversal_kind IS NULL)");
            table.HasCheckConstraint("ck_payment_attempts_chargeback_resolution_pairing",
                "(chargeback_resolved_at_utc IS NULL) = (chargeback_resolution IS NULL)");
            table.HasCheckConstraint("ck_payment_attempts_chargeback_resolution_supported",
                "chargeback_resolution IS NULL OR chargeback_resolution IN ('FinalWon', 'FinalLost')");
            table.HasCheckConstraint("ck_payment_attempts_lost_chargeback_reversed",
                "chargeback_resolution <> 'FinalLost' OR (status = 'Reversed' AND reversal_kind = 'FinalLostChargeback')");
        });
    }
}
