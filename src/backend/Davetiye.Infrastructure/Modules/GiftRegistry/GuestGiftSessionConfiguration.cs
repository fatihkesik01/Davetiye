using Davetiye.Domain.Modules.GiftRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

internal sealed class GuestGiftSessionConfiguration : IEntityTypeConfiguration<GuestGiftSession>
{
    public void Configure(EntityTypeBuilder<GuestGiftSession> builder)
    {
        builder.ToTable("guest_gift_sessions", table =>
        {
            table.HasCheckConstraint("ck_guest_gift_sessions_purpose", "purpose = 'gift-session'");
            table.HasCheckConstraint("ck_guest_gift_sessions_key_version_positive", "hmac_key_version > 0");
            table.HasCheckConstraint("ck_guest_gift_sessions_digest_length", "octet_length(hmac_digest) = 32");
            table.HasCheckConstraint("ck_guest_gift_sessions_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
        });

        builder.HasKey(session => session.Id);
        builder.HasAlternateKey(session => new { session.Id, session.InvitationId })
            .HasName("ak_guest_gift_sessions_id_invitation");
        builder.Property(session => session.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(session => session.HmacDigest).HasColumnType("bytea").IsRequired();
        builder.HasIndex(session => session.HmacDigest)
            .HasDatabaseName("ux_guest_gift_sessions_hmac_digest").IsUnique();
        builder.HasIndex(session => new { session.InvitationId, session.RevokedAt })
            .HasDatabaseName("ix_guest_gift_sessions_invitation_revoked_at");
    }
}
