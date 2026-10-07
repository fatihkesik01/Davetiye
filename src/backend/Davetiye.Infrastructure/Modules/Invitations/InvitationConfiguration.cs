using Davetiye.Domain.Modules.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Invitations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasKey(invitation => invitation.Id);

        // Creator reads, owner-reference checks and quota joins all default to live rows. M6's
        // explicit owner-scoped Trash operations may opt out; raw row locking also filters this.
        builder.HasQueryFilter(invitation => invitation.DeletedAt == null);

        builder.Property(invitation => invitation.PublicCode)
            .HasMaxLength(PublicInvitationCode.EncodedLength)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(invitation => invitation.PublicCode)
            .HasDatabaseName("ux_invitations_public_code")
            .IsUnique();

        builder.Property(invitation => invitation.State)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(invitation => invitation.TemplateKey)
            .HasMaxLength(100);

        // AccountId intentionally has no EF relationship/FK configured here: Account belongs to the
        // Identity & Accounts module, a different module from Invitations. Modules reference each
        // other only by id, never by cross-module EF navigation (ADR-0001), so this stays a plain
        // indexed Guid column - the same pattern
        // Davetiye.Infrastructure.Modules.PlansAndEntitlements.AccountPlanGrantConfiguration uses for
        // AccountPlanGrant.AccountId. The index supports the Application layer's mandatory
        // "AccountId == currentAccount" ownership filter on every Creator query/mutation
        // (docs/PHASE_0_PLAN.md §4).
        builder.HasIndex(invitation => invitation.AccountId);

        // Template pin fields are always set or cleared together (Invitation.PinTemplate never
        // allows one without the other). Templates is also a separate module (ADR-0001), so this is
        // a DB check constraint on plain columns, not a cross-module FK.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_invitations_template_pin_pairing",
                "(template_key IS NULL) = (renderer_version IS NULL)");
            table.HasCheckConstraint(
                "ck_invitations_public_code_format",
                "public_code ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_invitations_state_supported",
                "state IN ('Draft', 'Scheduled', 'Active', 'Paused', 'Expired')");
        });
    }
}
