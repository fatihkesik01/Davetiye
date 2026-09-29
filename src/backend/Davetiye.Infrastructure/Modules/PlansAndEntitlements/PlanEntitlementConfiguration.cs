using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

internal sealed class PlanEntitlementConfiguration : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.HasKey(entitlement => entitlement.Id);

        builder.Property(entitlement => entitlement.EntitlementKey)
            .IsRequired()
            .HasMaxLength(128);

        // A plan must not carry two rows for the same entitlement key.
        builder.HasIndex(entitlement => new { entitlement.PlanId, entitlement.EntitlementKey })
            .IsUnique();

        // Same-module reference: PlanEntitlement and Plan both belong to Plans & Entitlements.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.PlanId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
