namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// An Account's actual grant of a Plan, per ADR-0004. <see cref="AccountId"/> is a bare id, not an
/// EF navigation/FK to the Identity &amp; Accounts module's Account table: Plans &amp; Entitlements
/// and Identity &amp; Accounts are separate modules (docs/PHASE_1_EXECUTION.md §5), and modules
/// reference each other only by id, never by direct cross-module EF navigation.
///
/// This entity deliberately has no reference to any publication-window/invitation concept.
/// ADR-0004's "publication başlarken window/grant bağlantısı kalıcılaşır" describes a later
/// milestone's behavior, once Invitation exists (out of Phase 1 scope); this schema does not
/// invent a PublicationWindow entity ahead of that.
/// </summary>
public sealed class AccountPlanGrant
{
    private AccountPlanGrant()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid PlanId { get; private set; }

    public GrantSource Source { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public long Revision { get; private set; }

    public static AccountPlanGrant Create(
        Guid id,
        Guid accountId,
        Guid planId,
        GrantSource source,
        DateTimeOffset grantedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Grant id must not be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Grant must reference an account.", nameof(accountId));
        }

        if (planId == Guid.Empty)
        {
            throw new ArgumentException("Grant must reference a plan.", nameof(planId));
        }

        return new AccountPlanGrant
        {
            Id = id,
            AccountId = accountId,
            PlanId = planId,
            Source = source,
            GrantedAt = grantedAt
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("Grant is already revoked.");
        }

        RevokedAt = revokedAt;
    }
}
