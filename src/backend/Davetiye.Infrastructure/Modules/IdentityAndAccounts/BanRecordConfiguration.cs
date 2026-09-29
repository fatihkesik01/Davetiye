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

        builder.HasIndex(ban => ban.AccountId);

        // Same-module reference: BanRecord and Account both belong to Identity & Accounts.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(ban => ban.AccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
