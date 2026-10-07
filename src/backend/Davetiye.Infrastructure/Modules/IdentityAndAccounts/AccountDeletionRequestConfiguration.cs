using Davetiye.Domain.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

internal sealed class AccountDeletionRequestConfiguration : IEntityTypeConfiguration<AccountDeletionRequest>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRequest> builder)
    {
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(request => request.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(request => request.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(request => request.TokenHash).IsUnique();
        builder.HasIndex(request => request.AccountId)
            .HasDatabaseName("ux_account_deletion_requests_one_pending_per_account")
            .IsUnique()
            .HasFilter("status = 'Pending'");
        builder.HasIndex(request => new { request.AccountId, request.CreatedAtUtc });
        builder.HasOne<Account>().WithMany().HasForeignKey(request => request.AccountId)
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_account_deletion_requests_purpose", "purpose = 'AccountDeletion'");
            table.HasCheckConstraint("ck_account_deletion_requests_status",
                "status IN ('Pending', 'Consumed', 'Superseded', 'Expired')");
            table.HasCheckConstraint("ck_account_deletion_requests_token_hash",
                "length(token_hash) = 64 AND token_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_account_deletion_requests_expiry",
                "expires_at_utc > created_at_utc");
            table.HasCheckConstraint("ck_account_deletion_requests_consumed_pairing",
                "(status = 'Consumed') = (consumed_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_account_deletion_requests_consumed_range",
                "consumed_at_utc IS NULL OR (consumed_at_utc >= created_at_utc AND consumed_at_utc < expires_at_utc)");
            table.HasCheckConstraint("ck_account_deletion_requests_updated_range",
                "updated_at_utc >= created_at_utc");
        });
    }
}
