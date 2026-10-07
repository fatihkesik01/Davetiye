using Davetiye.Domain.Modules.Rsvp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Rsvp;

internal sealed class RsvpManageCapabilityConfiguration : IEntityTypeConfiguration<RsvpManageCapability>
{
    public void Configure(EntityTypeBuilder<RsvpManageCapability> builder)
    {
        builder.ToTable("rsvp_manage_capabilities", table =>
        {
            table.HasCheckConstraint("ck_rsvp_manage_capabilities_purpose", "purpose = 'rsvp-manage'");
            table.HasCheckConstraint("ck_rsvp_manage_capabilities_key_version_positive", "hmac_key_version > 0");
            table.HasCheckConstraint("ck_rsvp_manage_capabilities_digest_length", "octet_length(hmac_digest) = 32");
            table.HasCheckConstraint("ck_rsvp_manage_capabilities_expiry_after_created", "expires_at > created_at");
            table.HasCheckConstraint(
                "ck_rsvp_manage_capabilities_revoked_after_created",
                "revoked_at IS NULL OR revoked_at >= created_at");
        });

        builder.HasKey(capability => capability.Id);
        builder.Property(capability => capability.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(capability => capability.HmacDigest).HasColumnType("bytea").IsRequired();
        builder.HasIndex(capability => capability.HmacDigest)
            .HasDatabaseName("ux_rsvp_manage_capabilities_hmac_digest")
            .IsUnique();
        builder.HasIndex(capability => capability.SubmissionId)
            .HasDatabaseName("ux_rsvp_manage_capabilities_active_submission")
            .IsUnique()
            .HasFilter("revoked_at IS NULL");
        builder.HasOne<RsvpSubmission>()
            .WithMany()
            .HasForeignKey(capability => capability.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
