using Davetiye.Domain.Modules.Memories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Memories;

internal sealed class MemoryConfigurationConfiguration : IEntityTypeConfiguration<MemoryConfiguration>
{
    public void Configure(EntityTypeBuilder<MemoryConfiguration> builder)
    {
        builder.ToTable("memory_configurations", table =>
        {
            table.HasCheckConstraint("ck_memory_configurations_visibility", "visibility IN ('CreatorOnly', 'Public')");
            table.HasCheckConstraint("ck_memory_configurations_revision_nonnegative", "revision >= 0");
            table.HasCheckConstraint("ck_memory_configurations_updated_after_created", "updated_at >= created_at");
        });

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Visibility).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(configuration => configuration.Revision).IsConcurrencyToken();
        builder.HasIndex(configuration => configuration.InvitationId)
            .HasDatabaseName("ux_memory_configurations_invitation")
            .IsUnique();
    }
}
