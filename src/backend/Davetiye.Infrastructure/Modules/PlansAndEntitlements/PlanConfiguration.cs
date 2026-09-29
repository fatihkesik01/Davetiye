using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Key)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(plan => plan.Key)
            .IsUnique();

        builder.Property(plan => plan.DisplayName)
            .IsRequired()
            .HasMaxLength(200);
    }
}
