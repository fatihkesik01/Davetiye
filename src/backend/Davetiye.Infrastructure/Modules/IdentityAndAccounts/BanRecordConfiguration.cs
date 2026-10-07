using Davetiye.Domain.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

internal sealed class BanRecordConfiguration : IEntityTypeConfiguration<BanRecord>
{
    public void Configure(EntityTypeBuilder<BanRecord> builder)
    {
        builder.HasKey(ban => ban.Id);

        builder.Property(ban => ban.Reason)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(ban => ban.InternalNote)
            .HasMaxLength(BanRecord.MaxInternalNoteLength);

        builder.HasIndex(ban => ban.AccountId, "ix_ban_records_account_id")
            .HasDatabaseName("ix_ban_records_account_id");
        builder.HasIndex(ban => ban.AccountId, "ux_ban_records_one_active_per_account")
            .HasDatabaseName("ux_ban_records_one_active_per_account")
            .IsUnique()
            .HasFilter("revoked_at IS NULL");

        // Same-module reference: BanRecord and Account both belong to Identity & Accounts.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(ban => ban.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_ban_records_internal_note_nonblank",
            "internal_note IS NULL OR (length(internal_note) <= 2000 AND btrim(internal_note) <> '')"));
    }
}
