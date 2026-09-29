using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(setting => setting.Id);

        builder.Property(setting => setting.Key)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(setting => setting.Key)
            .IsUnique();

        builder.Property(setting => setting.ValueType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(setting => setting.Value)
            .IsRequired()
            .HasMaxLength(4000);
    }
}
