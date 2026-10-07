using Davetiye.Domain.Modules.GiftRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

internal sealed class GiftReservationConfiguration : IEntityTypeConfiguration<GiftReservation>
{
    public void Configure(EntityTypeBuilder<GiftReservation> builder)
    {
        builder.ToTable("gift_reservations", table =>
        {
            table.HasCheckConstraint("ck_gift_reservations_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_gift_reservations_guest_full_name", "char_length(guest_full_name) BETWEEN 1 AND 200 AND btrim(guest_full_name) <> ''");
            table.HasCheckConstraint("ck_gift_reservations_email_length", "email IS NULL OR char_length(email) BETWEEN 3 AND 320");
            table.HasCheckConstraint("ck_gift_reservations_phone_length", "phone IS NULL OR char_length(phone) BETWEEN 1 AND 32");
        });

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.GuestFullName).HasMaxLength(200).IsRequired();
        builder.Property(reservation => reservation.Email).HasMaxLength(320);
        builder.Property(reservation => reservation.Phone).HasMaxLength(32);
        builder.HasIndex(reservation => reservation.InvitationId)
            .HasDatabaseName("ix_gift_reservations_invitation");

        builder.HasOne<GiftItem>().WithMany()
            .HasForeignKey(reservation => new { reservation.GiftItemId, reservation.InvitationId })
            .HasPrincipalKey(item => new { item.Id, item.InvitationId })
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.HasOne<GuestGiftSession>().WithMany()
            .HasForeignKey(reservation => new { reservation.GuestGiftSessionId, reservation.InvitationId })
            .HasPrincipalKey(session => new { session.Id, session.InvitationId })
            .HasConstraintName("fk_gift_reservations_gift_session")
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
    }
}
