using Davetiye.Domain.Modules.PlansAndEntitlements;

namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>Account-wide reservation read model shared across the publication boundary.</summary>
public sealed record PublicationQuotaSlot(
    Guid InvitationId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public enum PublicationAdmissionDenial
{
    None,
    PublishDurationExceeded,
    ActiveInvitationQuotaExceeded
}

public sealed record PublicationAdmissionDecision(bool IsAllowed, PublicationAdmissionDenial Denial);

/// <summary>Plans-owned admission rules exposed without leaking domain implementation types.</summary>
public static class PublicationAdmission
{
    public static PublicationAdmissionDecision EvaluateQuota(
        Guid invitationId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        long maxPublishDays,
        long maxActiveInvitations,
        IEnumerable<PublicationQuotaSlot> slots,
        bool enforceDuration = true)
    {
        var domainSlots = new List<AccountPublicationSlot>();
        foreach (var slot in slots)
        {
            domainSlots.Add(new AccountPublicationSlot(
                slot.InvitationId, new PublicationInterval(slot.StartsAt, slot.EndsAt)));
        }

        var decision = PublicationQuotaPolicy.Evaluate(
            invitationId,
            new PublicationInterval(startsAt, endsAt),
            maxPublishDays,
            maxActiveInvitations,
            domainSlots,
            enforceDuration);

        return new PublicationAdmissionDecision(decision.IsAllowed, decision.Denial switch
        {
            PublicationQuotaDenial.None => PublicationAdmissionDenial.None,
            PublicationQuotaDenial.PublishDurationExceeded => PublicationAdmissionDenial.PublishDurationExceeded,
            _ => PublicationAdmissionDenial.ActiveInvitationQuotaExceeded
        });
    }

    public static bool AllocationMatchesRequest(
        EffectiveEntitlementSnapshot entitlements,
        Guid? requestedGrantId,
        Guid accountId,
        Guid invitationId,
        bool scheduled)
    {
        if (entitlements.AccountId != accountId || entitlements.GrantId == Guid.Empty ||
            (requestedGrantId is not null && entitlements.GrantId != requestedGrantId.Value) ||
            (requestedGrantId is null && entitlements.GrantSource != GrantSource.Free))
        {
            return false;
        }

        if (entitlements.GrantSource == GrantSource.OrganizationSubscription)
        {
            return entitlements.AssignedInvitationId is null &&
                   entitlements.ReservedAt is null && entitlements.ConsumedAt is null;
        }

        return entitlements.AssignedInvitationId == invitationId &&
               entitlements.ReservedAt is not null &&
               (scheduled ? entitlements.ConsumedAt is null : entitlements.ConsumedAt is not null);
    }
}
