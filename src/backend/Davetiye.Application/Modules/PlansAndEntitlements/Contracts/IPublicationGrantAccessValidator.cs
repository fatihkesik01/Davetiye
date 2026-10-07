using Davetiye.Domain.Modules.PlansAndEntitlements;

namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>
/// Read-only accepted-window access. Current plan/catalog limits never revoke an admitted window;
/// ownership, assignment, reservation, grant timing and explicit revocation remain authoritative.
/// </summary>
public interface IPublicationGrantAccessValidator
{
    Task<PublicationGrantAccessSnapshot?> ReadAsync(Guid accountId, Guid grantId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
        DateTimeOffset evaluatedAtUtc, int limit, CancellationToken cancellationToken);
}

public sealed record PublicationGrantAccessSnapshot(
    Guid GrantId, Guid AccountId, bool SupportsAcceptedWindow, Guid? AssignedInvitationId,
    DateTimeOffset GrantedAt, DateTimeOffset? ReservedAt, DateTimeOffset? ConsumedAt, DateTimeOffset? RevokedAt,
    GrantSource GrantSource = GrantSource.IndividualPurchase,
    DateTimeOffset? OrganizationSubscriptionPaidThroughAtUtc = null,
    Guid PlanId = default);

/// <summary>Evaluates the read snapshot with the same final UTC instant as the invitation window.</summary>
public static class PublicationGrantAccessPolicy
{
    public static bool IsAllowed(PublicationGrantAccessSnapshot? grant, Guid accountId, Guid invitationId,
        Guid grantId, DateTimeOffset windowStartsAtUtc, DateTimeOffset nowUtc) =>
        grant is not null && accountId != Guid.Empty && invitationId != Guid.Empty && grantId != Guid.Empty &&
        grant.GrantId == grantId && grant.AccountId == accountId && grant.SupportsAcceptedWindow &&
        grant.GrantedAt <= windowStartsAtUtc &&
        (grant.RevokedAt is null || (grant.RevokedAt >= grant.GrantedAt && grant.RevokedAt > nowUtc)) &&
        (grant.GrantSource == GrantSource.OrganizationSubscription
            ? grant.AssignedInvitationId is null && grant.ReservedAt is null && grant.ConsumedAt is null &&
              grant.OrganizationSubscriptionPaidThroughAtUtc is { } paidThrough &&
              paidThrough > nowUtc && paidThrough > windowStartsAtUtc
            : grant.AssignedInvitationId == invitationId && grant.ReservedAt is not null &&
              grant.ReservedAt >= grant.GrantedAt && grant.ReservedAt <= windowStartsAtUtc &&
              (grant.ConsumedAt is null || (grant.ConsumedAt >= grant.ReservedAt && grant.ConsumedAt <= nowUtc)));

    /// <summary>Identifies an expired/missing Organization subscription for a known shared-grant candidate.</summary>
    public static bool IsExpiredOrganizationGrant(PublicationGrantAccessSnapshot? grant, Guid accountId,
        Guid invitationId, Guid grantId, DateTimeOffset windowStartsAtUtc, DateTimeOffset nowUtc)
    {
        if (grant is null)
            return true;
        if (grant.GrantSource != GrantSource.OrganizationSubscription)
            return false;

        var paidThrough = grant.OrganizationSubscriptionPaidThroughAtUtc;
        return !IsAllowed(grant, accountId, invitationId, grantId, windowStartsAtUtc, nowUtc) &&
               (paidThrough is null || paidThrough <= nowUtc);
    }
}
