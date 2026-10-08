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
            .HasDefaultValue("light")
            .IsRequired();

        builder.Property(user => user.PreferredAvatar)
            .HasMaxLength(16);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_locale",
                "preferred_locale IN ('tr', 'en')");
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_color_theme",
                "preferred_color_theme IN ('kutlio', 'sage', 'rose', 'ocean', 'plum', 'gold')");
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_appearance",
                "preferred_appearance IN ('system', 'light', 'dark')");
            table.HasCheckConstraint(
                "ck_asp_net_users_preferred_avatar",
                "preferred_avatar IS NULL OR preferred_avatar IN ('sunny', 'mint', 'berry', 'sky', 'coral', 'lilac', 'amber', 'forest', 'night', 'rose', 'slate', 'peach')");
        });
    }
}
