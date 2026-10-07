namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// An Account's actual grant of a Plan, per ADR-0004. <see cref="AccountId"/> is a bare id, not an
/// EF navigation/FK to the Identity &amp; Accounts module's Account table: Plans &amp; Entitlements
/// and Identity &amp; Accounts are separate modules (docs/PHASE_1_PLAN.md §5), and modules
/// reference each other only by id, never by direct cross-module EF navigation.
///
/// Invitation assignment is represented only by its id. Free and individual-purchase grants are
/// reservable single-use rights; an Organization subscription is a shared grant and is not assigned
/// through these methods. The later PublicationWindow remains owned by Invitations.
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

    /// <summary>Immutable commercial shape accepted when this grant was created.</summary>
    public PlanBillingKind BillingKindAtGrant { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? AssignedInvitationId { get; private set; }

    public DateTimeOffset? ReservedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

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

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported grant source.");
        }

        return new AccountPlanGrant
        {
            Id = id,
            AccountId = accountId,
            PlanId = planId,
            Source = source,
            BillingKindAtGrant = source switch
            {
                GrantSource.Free => PlanBillingKind.Free,
                GrantSource.IndividualPurchase => PlanBillingKind.OneTime,
                GrantSource.OrganizationSubscription => PlanBillingKind.Monthly,
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported grant source.")
            },
            GrantedAt = grantedAt
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("Grant is already revoked.");
        }

        if (revokedAt < GrantedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(revokedAt), "A grant cannot be revoked before it was granted.");
        }

        RevokedAt = revokedAt;
        Revision++;
    }

    public bool IsRevokedAt(DateTimeOffset instant) => RevokedAt is not null && RevokedAt <= instant;

    public void ReserveForInvitation(Guid invitationId, DateTimeOffset reservedAt)
    {
        EnsureAssignable(invitationId);

        if (ConsumedAt is not null)
        {
            if (AssignedInvitationId == invitationId)
            {
                return;
            }

            throw new InvalidOperationException("A consumed grant cannot be assigned to another invitation.");
        }

        if (AssignedInvitationId is not null && AssignedInvitationId != invitationId)
        {
            throw new InvalidOperationException("The grant is already reserved for another invitation.");
        }

        if (AssignedInvitationId == invitationId && ReservedAt is not null)
        {
            return;
        }

        if (reservedAt < GrantedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedAt), "A grant cannot be reserved before it was granted.");
        }

        AssignedInvitationId = invitationId;
        ReservedAt ??= reservedAt;
        Revision++;
    }

    public void ReleaseReservation(Guid invitationId)
    {
        // Cancellation reduces usage and must remain possible after a grant was revoked.
        if (invitationId == Guid.Empty || Source == GrantSource.OrganizationSubscription)
        {
            throw new ArgumentException("Reservation release requires an individual invitation grant.", nameof(invitationId));
        }

        if (AssignedInvitationId is null)
        {
            return;
        }

        if (AssignedInvitationId != invitationId)
        {
            throw new InvalidOperationException("The grant is reserved for another invitation.");
        }

        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException("A consumed grant reservation cannot be released.");
        }

        AssignedInvitationId = null;
        ReservedAt = null;
        Revision++;
    }

    public void ConsumeForInvitation(Guid invitationId, DateTimeOffset consumedAt)
    {
        EnsureAssignable(invitationId);

        if (AssignedInvitationId is not null && AssignedInvitationId != invitationId)
        {
            throw new InvalidOperationException("The grant is assigned to another invitation.");
        }

        if (ConsumedAt is not null)
        {
            return;
        }

        if (consumedAt < GrantedAt || (ReservedAt is not null && consumedAt < ReservedAt))
        {
            throw new ArgumentOutOfRangeException(nameof(consumedAt), "A grant cannot be consumed before it was granted or reserved.");
        }

        // Immediate publication reserves and consumes in the same atomic operation.
        AssignedInvitationId = invitationId;
        ReservedAt ??= consumedAt;
        ConsumedAt = consumedAt;
        Revision++;
    }

    private void EnsureAssignable(Guid invitationId)
    {
        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty.", nameof(invitationId));
        }

        if (Source == GrantSource.OrganizationSubscription)
        {
            throw new InvalidOperationException("A shared Organization grant is not assigned to one invitation.");
        }

        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("A revoked grant cannot be reserved or consumed.");
        }
    }
}
