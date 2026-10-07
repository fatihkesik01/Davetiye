using Davetiye.Domain.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Media;

internal sealed class MediaPlacementConfiguration : IEntityTypeConfiguration<MediaPlacement>
{
    public void Configure(EntityTypeBuilder<MediaPlacement> builder)
    {
        builder.HasKey(placement => placement.Id);
        builder.Property(placement => placement.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(placement => placement.MediaAssetId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Each asset appears at most once in each role, but can have both a Cover and a Gallery
        // placement. Invitation-wide cover cardinality is intentionally left undecided.
        builder.HasIndex(placement => new { placement.MediaAssetId, placement.Role })
            .HasDatabaseName("ux_media_placements_asset_role")
            .IsUnique();
        builder.HasIndex(placement => new { placement.Role, placement.SortOrder })
            .HasDatabaseName("ix_media_placements_role_sort_order");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_media_placements_supported_role", "role IN ('Cover', 'Gallery')");
            table.HasCheckConstraint("ck_media_placements_nonnegative_sort_order", "sort_order >= 0");
        });
    }
}
