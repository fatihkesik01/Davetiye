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

        builder.Property(plan => plan.Description)
            .HasMaxLength(Plan.DescriptionMaxLength);

        builder.Property(plan => plan.BillingKind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(plan => plan.PriceAmount)
            .IsRequired();

        builder.Property(plan => plan.Currency)
            .IsRequired();

        builder.Property(plan => plan.Revision).IsConcurrencyToken();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_plans_price_non_negative",
                "price_amount >= 0");
            table.HasCheckConstraint(
                "ck_plans_currency_iso_length",
                "currency ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint(
                "ck_plans_billing_kind_supported",
                "billing_kind IN ('Free', 'OneTime', 'Monthly')");
            table.HasCheckConstraint(
                "ck_plans_free_price",
                "billing_kind <> 'Free' OR price_amount = 0");
        });
    }
}
