using Davetiye.Domain.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

internal sealed class AccountDeletionWorkConfiguration : IEntityTypeConfiguration<AccountDeletionWork>
{
    public void Configure(EntityTypeBuilder<AccountDeletionWork> builder)
    {
        builder.HasKey(work => work.Id);
        builder.Property(work => work.Status).HasMaxLength(16).IsRequired();
        builder.Property(work => work.LastErrorKind).HasMaxLength(64);
        builder.HasIndex(work => work.AccountId).IsUnique();
        builder.HasIndex(work => new { work.Status, work.NextAttemptAtUtc });
        builder.HasOne<Account>().WithMany().HasForeignKey(work => work.AccountId)
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_account_deletion_works_status",
                "status IN ('Queued', 'Retrying', 'Completed')");
            table.HasCheckConstraint("ck_account_deletion_works_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_account_deletion_works_retry_pairing",
                "(status = 'Retrying') = (next_attempt_at_utc IS NOT NULL AND last_error_kind IS NOT NULL AND last_error_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_account_deletion_works_completion_pairing",
                "(status = 'Completed') = (completed_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_account_deletion_works_completion_checkpoints",
                "completed_at_utc IS NULL OR (subscription_cancellations_queued_at_utc IS NOT NULL " +
                "AND invitations_purge_queued_at_utc IS NOT NULL AND identity_sanitized_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_account_deletion_works_checkpoint_order",
                "(subscription_cancellations_queued_at_utc IS NULL OR subscription_cancellations_queued_at_utc >= started_at_utc) " +
                "AND (invitations_purge_queued_at_utc IS NULL OR invitations_purge_queued_at_utc >= started_at_utc) " +
                "AND (identity_sanitized_at_utc IS NULL OR identity_sanitized_at_utc >= started_at_utc) " +
                "AND (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)");
            table.HasCheckConstraint("ck_account_deletion_works_error_kind",
                "last_error_kind IS NULL OR (length(last_error_kind) BETWEEN 1 AND 64 AND last_error_kind ~ '^[A-Za-z0-9._-]+$')");
        });
    }
}
