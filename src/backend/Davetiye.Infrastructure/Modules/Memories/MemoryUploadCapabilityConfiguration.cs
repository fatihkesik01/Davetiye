using Davetiye.Domain.Modules.Memories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Memories;

internal sealed class MemoryUploadCapabilityConfiguration : IEntityTypeConfiguration<MemoryUploadCapability>
{
    public void Configure(EntityTypeBuilder<MemoryUploadCapability> builder)
    {
        builder.ToTable("memory_upload_capabilities", table =>
        {
            table.HasCheckConstraint("ck_memory_upload_capabilities_purpose", "purpose = 'memory-upload'");
            table.HasCheckConstraint("ck_memory_upload_capabilities_key_version_positive", "hmac_key_version > 0");
            table.HasCheckConstraint("ck_memory_upload_capabilities_digest_length", "octet_length(hmac_digest) = 32");
            // Mirrors MemoryInputLimits.HardMaxUploadCapabilityLifetimeMinutes (15).
            table.HasCheckConstraint("ck_memory_upload_capabilities_expiry_window",
                "expires_at > created_at AND expires_at <= created_at + interval '15 minutes'");
            table.HasCheckConstraint("ck_memory_upload_capabilities_consumed_after_created",
                "consumed_at IS NULL OR consumed_at >= created_at");
            table.HasCheckConstraint("ck_memory_upload_capabilities_revoked_after_created",
                "revoked_at IS NULL OR revoked_at >= created_at");
        });

        builder.HasKey(capability => capability.Id);
        builder.Property(capability => capability.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(capability => capability.HmacDigest).HasColumnType("bytea").IsRequired();
        builder.HasIndex(capability => capability.HmacDigest)
            .HasDatabaseName("ux_memory_upload_capabilities_hmac_digest")
            .IsUnique();
        builder.HasIndex(capability => capability.MemoryId)
            .HasDatabaseName("ux_memory_upload_capabilities_active_memory")
            .IsUnique()
            .HasFilter("revoked_at IS NULL AND consumed_at IS NULL");
        builder.HasOne<Memory>()
            .WithMany()
            .HasForeignKey(capability => capability.MemoryId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
