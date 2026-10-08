using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.PreferredLocale)
            .HasMaxLength(2)
            .HasDefaultValue("tr")
            .IsRequired();

        builder.Property(user => user.PreferredColorTheme)
            .HasMaxLength(6)
            .HasDefaultValue("kutlio")
            .IsRequired();

        builder.Property(user => user.PreferredAppearance)
            .HasMaxLength(6)
            .HasDefaultValue("system")
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_locale",
                "preferred_locale IN ('tr', 'en')");
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_color_theme",
                "preferred_color_theme IN ('kutlio', 'sage', 'rose', 'ocean', 'plum')");
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_appearance",
                "preferred_appearance IN ('system', 'light', 'dark')");
        });
    }
}
