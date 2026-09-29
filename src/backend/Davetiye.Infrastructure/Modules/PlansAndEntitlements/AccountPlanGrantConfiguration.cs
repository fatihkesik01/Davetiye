using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

internal sealed class AccountPlanGrantConfiguration : IEntityTypeConfiguration<AccountPlanGrant>
{
    public void Configure(EntityTypeBuilder<AccountPlanGrant> builder)
    {
        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.Source)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // AccountId intentionally has no EF relationship/FK configured here: Account belongs to
        // the Identity & Accounts module, a different module from Plans & Entitlements. Modules
        // reference each other only by id, never by cross-module EF navigation
        // (docs/PHASE_1_EXECUTION.md §5), so this stays a plain indexed Guid column.
        builder.HasIndex(grant => grant.AccountId);

        // Same-module reference: AccountPlanGrant and Plan both belong to Plans & Entitlements.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(grant => grant.PlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
