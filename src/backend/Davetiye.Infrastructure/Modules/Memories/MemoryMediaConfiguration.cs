using Davetiye.Domain.Modules.Memories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Memories;

internal sealed class MemoryMediaConfiguration : IEntityTypeConfiguration<MemoryMedia>
{
    public void Configure(EntityTypeBuilder<MemoryMedia> builder)
    {
        builder.ToTable("memory_media", table =>
        {
            // Mirrors MemoryInputLimits.HardMaxMediaPerMemory (3).
            table.HasCheckConstraint("ck_memory_media_ordinal_range", "ordinal BETWEEN 0 AND 2");
        });

        builder.HasKey(media => media.Id);
        // MediaAssetId is an ID-only reference to a Media-owned row; deliberately no foreign key.
        builder.HasIndex(media => media.MediaAssetId)
            .HasDatabaseName("ux_memory_media_asset")
            .IsUnique();
        builder.HasIndex(media => new { media.MemoryId, media.Ordinal })
            .HasDatabaseName("ux_memory_media_memory_ordinal")
            .IsUnique();
    }
}
