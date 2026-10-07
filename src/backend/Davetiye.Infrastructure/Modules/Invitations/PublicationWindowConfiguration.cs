using Davetiye.Domain.Modules.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Invitations;

internal sealed class PublicationWindowConfiguration : IEntityTypeConfiguration<PublicationWindow>
{
    public void Configure(EntityTypeBuilder<PublicationWindow> builder)
    {
        builder.HasKey(window => window.Id);

        builder.Property(window => window.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<Invitation>()
            .WithMany()
            .HasForeignKey(window => window.InvitationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // GrantId intentionally remains a plain cross-module identifier. Plans & Entitlements
        // owns AccountPlanGrant; Invitations owns PublicationWindow (ADR-0001).
        builder.HasIndex(window => window.GrantId);

        builder.HasIndex(window => window.InvitationId)
            .HasDatabaseName("ux_publication_windows_current_invitation")
            .IsUnique()
            .HasFilter("is_current");

        builder.HasIndex(window => new { window.IsCurrent, window.StartsAt, window.EndsAt })
            .HasDatabaseName("ix_publication_windows_current_interval");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_publication_windows_interval",
                "starts_at < ends_at");
            table.HasCheckConstraint(
                "ck_publication_windows_timezone_not_blank",
                "length(btrim(time_zone_id)) > 0");
        });
    }
}
