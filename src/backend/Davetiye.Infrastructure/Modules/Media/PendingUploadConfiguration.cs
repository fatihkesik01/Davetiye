using Davetiye.Domain.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Media;

internal sealed class PendingUploadConfiguration : IEntityTypeConfiguration<PendingUpload>
{
    public void Configure(EntityTypeBuilder<PendingUpload> builder)
    {
        builder.HasKey(upload => upload.Id);
        builder.Property(upload => upload.RequestedPresentationRole).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(upload => upload.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(upload => upload.MediaAssetId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(upload => upload.MediaAssetId)
            .HasDatabaseName("ux_pending_uploads_one_open_per_asset")
            .IsUnique()
            .HasFilter("consumed_at IS NULL AND cancelled_at IS NULL");
        builder.HasIndex(upload => upload.IdempotencyKey)
            .HasDatabaseName("ux_pending_uploads_idempotency_key")
            .IsUnique();
        builder.HasIndex(upload => new { upload.ConsumedAt, upload.CancelledAt, upload.ExpiresAt })
            .HasDatabaseName("ix_pending_uploads_open_expiry");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pending_uploads_expiry_after_creation", "created_at < expires_at");
            table.HasCheckConstraint("ck_pending_uploads_request_values", "declared_byte_length > 0 AND maximum_byte_length >= declared_byte_length AND maximum_duration_seconds >= 0 AND length(request_fingerprint) = 64 AND requested_presentation_role IN ('Cover', 'Gallery')");
            table.HasCheckConstraint(
                "ck_pending_uploads_terminal_state_pairing",
                "consumed_at IS NULL OR cancelled_at IS NULL");
        });
    }
}
