using Davetiye.Domain.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Media;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.QuotaScope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(asset => asset.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(asset => asset.State).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(asset => asset.ProviderObjectReference).HasMaxLength(512);
        builder.Property(asset => asset.DetectedContentType).HasMaxLength(128);

        builder.HasIndex(asset => new { asset.InvitationId, asset.QuotaScope, asset.Kind, asset.State })
            .HasDatabaseName("ix_media_assets_invitation_quota_kind_state");
        builder.HasIndex(asset => new { asset.State, asset.CreatedAt })
            .HasDatabaseName("ix_media_assets_state_created_at");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_media_assets_supported_values",
                "quota_scope IN ('Creator', 'Guest') AND kind IN ('Image', 'Video') AND state IN " +
                "('PendingUpload', 'Processing', 'Ready', 'Rejected', 'PendingDeletion', 'Deleted')");
            table.HasCheckConstraint(
                "ck_media_assets_ready_metadata",
                "state <> 'Ready' OR (provider_object_reference IS NOT NULL AND detected_content_type IS NOT NULL " +
                "AND byte_length > 0 AND ready_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_media_assets_deletion_timestamps",
                "state NOT IN ('PendingDeletion', 'Deleted') OR deletion_requested_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_media_assets_deleted_timestamp",
                "state <> 'Deleted' OR provider_deleted_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_media_assets_positive_measurements",
                "(byte_length IS NULL OR byte_length > 0) AND (duration_seconds IS NULL OR duration_seconds > 0)");
        });
    }
}
