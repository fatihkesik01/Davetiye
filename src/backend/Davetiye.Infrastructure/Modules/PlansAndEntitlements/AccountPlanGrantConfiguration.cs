using Davetiye.Domain.Modules.PlansAndEntitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

internal sealed class AccountPlanGrantConfiguration : IEntityTypeConfiguration<AccountPlanGrant>
{
    public void Configure(EntityTypeBuilder<AccountPlanGrant> builder)
    {
        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.Source)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(grant => grant.BillingKindAtGrant)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // AccountId intentionally has no EF relationship/FK configured here: Account belongs to
        // the Identity & Accounts module, a different module from Plans & Entitlements. Modules
        // reference each other only by id, never by cross-module EF navigation
        // (docs/PHASE_1_PLAN.md §5), so this stays a plain indexed Guid column.
        builder.HasIndex(grant => grant.AccountId);

        // The accepted P3-M1 free entitlement is lifetime-single-use per account. Revoking or
        // consuming the row must not allow another Free row to be issued later, so the filter is
        // deliberately only on Source and not on RevokedAt/ConsumedAt.
        builder.HasIndex(grant => grant.AccountId)
            .HasDatabaseName("ux_account_plan_grants_lifetime_free_per_account")
            .IsUnique()
            .HasFilter("source = 'Free'");

        builder.HasIndex(grant => new { grant.AccountId, grant.RevokedAt, grant.ConsumedAt });

        // Same-module reference: AccountPlanGrant and Plan both belong to Plans & Entitlements.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(grant => grant.PlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // AssignedInvitationId remains a plain cross-module id (ADR-0001), just like AccountId.
        // These checks make partially-written reservation states impossible even if a future
        // writer bypasses the domain methods.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_account_plan_grants_reservation_pairing",
                "(assigned_invitation_id IS NULL) = (reserved_at IS NULL)");
            table.HasCheckConstraint(
                "ck_account_plan_grants_consumption_requires_reservation",
                "consumed_at IS NULL OR reserved_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_account_plan_grants_consumption_order",
                "consumed_at IS NULL OR consumed_at >= reserved_at");
            table.HasCheckConstraint(
                "ck_account_plan_grants_shared_grant_unassigned",
                "source <> 'OrganizationSubscription' OR " +
                "(assigned_invitation_id IS NULL AND reserved_at IS NULL AND consumed_at IS NULL)");
        });
    }
}
