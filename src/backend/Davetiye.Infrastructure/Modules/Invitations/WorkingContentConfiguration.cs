using Davetiye.Domain.Modules.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>
/// Enforces "at most one WorkingContent per Invitation" as a real DB constraint (a unique index on
/// the FK, which EF Core also creates automatically for a required one-to-one relationship) - not
/// just an application-level check, which would race under concurrent requests. The "exactly one"
/// half of docs/PHASE_0_PLAN.md §3's "Invitation 1──1 WorkingContent" (every Invitation always has
/// one) is a transactional-creation responsibility for M3's Application layer, the same way
/// Davetiye.Infrastructure.Modules.IdentityAndAccounts.AccountConfiguration's Account/ApplicationUser
/// one-to-one relies on Application code to create both together.
/// </summary>
internal sealed class WorkingContentConfiguration : IEntityTypeConfiguration<WorkingContent>
{
    public void Configure(EntityTypeBuilder<WorkingContent> builder)
    {
        builder.HasKey(content => content.Id);

        builder.Property(content => content.Content)
            .HasColumnType("jsonb")
            .IsRequired();

        // Same-module reference: WorkingContent and Invitation both belong to Invitations.
        // WorkingContent has no lifecycle independent of its Invitation, so it is deleted along with
        // it.
        builder.HasOne<Invitation>()
            .WithOne()
            .HasForeignKey<WorkingContent>(content => content.InvitationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
