using Davetiye.Domain.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

internal sealed class AccountConsentRecordConfiguration : IEntityTypeConfiguration<AccountConsentRecord>
{
    public void Configure(EntityTypeBuilder<AccountConsentRecord> builder)
    {
        builder.ToTable("account_consent_records", table =>
        {
            table.HasCheckConstraint("ck_account_consent_records_version_nonblank", "length(version) BETWEEN 1 AND 100 AND btrim(version) <> ''");
            table.HasCheckConstraint("ck_account_consent_records_kind", "kind IN ('ServiceNoticeAcknowledgement', 'MarketingPreference')");
            table.HasCheckConstraint("ck_account_consent_records_source", "source IN ('EmailPasswordSignup', 'GoogleSignup', 'AccountSettings', 'ExistingAccountAcknowledgement')");
            table.HasCheckConstraint("ck_account_consent_records_service_notice_granted", "kind <> 'ServiceNoticeAcknowledgement' OR granted = TRUE");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(record => record.Version).HasMaxLength(100).IsRequired();
        builder.Property(record => record.Source).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(record => record.RecordedAt).IsRequired();
        builder.HasIndex(record => new { record.AccountId, record.Kind, record.RecordedAt });
        builder.HasOne<Account>().WithMany().HasForeignKey(record => record.AccountId)
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
    }
}
