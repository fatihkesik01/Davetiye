using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.PlansAndEntitlements;

public sealed class EffectiveEntitlementResolver(IEntitlementGrantReader grantReader, IClock clock)
    : IEffectiveEntitlementResolver
{
    public async Task<EntitlementResolutionResult> ResolveAsync(
        EntitlementResolutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AccountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(context));
        }

        if (context.GrantId == Guid.Empty)
        {
            throw new ArgumentException("Grant id must not be empty.", nameof(context));
        }

        if (context.InvitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty when supplied.", nameof(context));
        }

        if (!Enum.IsDefined(context.Action))
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Unsupported entitlement action.");
        }

        var grant = await grantReader.FindOwnedGrantAsync(
            context.AccountId,
            context.GrantId,
            cancellationToken);

        if (grant is null || grant.AccountId != context.AccountId || grant.GrantId != context.GrantId)
        {
            return EntitlementResolutionResult.Denied(
                EntitlementResolutionDenial.GrantNotFoundOrNotOwned);
        }

        if (!HasValidGrantShape(grant) || !TryReadAllValues(grant.Values, out var values))
        {
            return EntitlementResolutionResult.Denied(EntitlementResolutionDenial.InvalidConfiguration);
        }

        var nowUtc = clock.UtcNow.ToUniversalTime();
        if (grant.GrantedAt > nowUtc ||
            (grant.RevokedAt is not null && grant.RevokedAt <= nowUtc) ||
            grant.GrantSource == GrantSource.OrganizationSubscription &&
            (grant.OrganizationSubscriptionPaidThroughAtUtc is null ||
             grant.OrganizationSubscriptionPaidThroughAtUtc <= nowUtc))
        {
            return EntitlementResolutionResult.Denied(EntitlementResolutionDenial.GrantNotEffective);
        }

        if (!grant.PlanIsActive && RequiresCurrentPlanAdmission(context.Action))
        {
            return EntitlementResolutionResult.Denied(EntitlementResolutionDenial.GrantNotEffective);
        }

        if (grant.AssignedInvitationId is not null &&
            context.InvitationId != grant.AssignedInvitationId)
        {
            return EntitlementResolutionResult.Denied(
                EntitlementResolutionDenial.AssignedToAnotherInvitation);
        }

        if (!ActionMatchesGrantState(context, grant))
        {
            return EntitlementResolutionResult.Denied(
                EntitlementResolutionDenial.ActionNotAllowedByGrantState);
        }

        return EntitlementResolutionResult.Granted(new EffectiveEntitlementSnapshot(
            grant.AccountId,
            grant.GrantId,
            grant.PlanId,
            grant.PlanKey,
            grant.PlanBillingKind,
            grant.GrantSource,
            grant.AssignedInvitationId,
            grant.ReservedAt,
            grant.ConsumedAt,
            Numeric(values, EntitlementCatalog.MaxPublishDays),
            Numeric(values, EntitlementCatalog.MaxActiveInvitations),
            Numeric(values, EntitlementCatalog.MaxImages),
            Numeric(values, EntitlementCatalog.MaxVideos),
            Numeric(values, EntitlementCatalog.MaxImageSizeMb),
            Numeric(values, EntitlementCatalog.MaxVideoSizeMb),
            Numeric(values, EntitlementCatalog.MaxVideoDurationSeconds),
            Numeric(values, EntitlementCatalog.MaxRsvpResponses),
            Boolean(values, EntitlementCatalog.MemoriesEnabled),
            Boolean(values, EntitlementCatalog.GiftRegistryEnabled),
            Boolean(values, EntitlementCatalog.PremiumTemplatesEnabled),
            Numeric(values, EntitlementCatalog.MaxGuestImages),
            Numeric(values, EntitlementCatalog.MaxGuestVideos),
            Numeric(values, EntitlementCatalog.MaxGuestImageSizeMb),
            Numeric(values, EntitlementCatalog.MaxGuestVideoSizeMb),
            Numeric(values, EntitlementCatalog.MaxGuestVideoDurationSeconds)));
    }

    private static bool ActionMatchesGrantState(
        EntitlementResolutionContext context,
        EntitlementGrantSnapshot grant)
    {
        if (grant.GrantSource == GrantSource.OrganizationSubscription)
        {
            return true;
        }

        return context.Action switch
        {
            PublicationEntitlementAction.PublishNow or
            PublicationEntitlementAction.Schedule or
            PublicationEntitlementAction.Reactivate => grant.ConsumedAt is null,
            PublicationEntitlementAction.Reschedule =>
                grant.AssignedInvitationId == context.InvitationId &&
                grant.ReservedAt is not null &&
                grant.ConsumedAt is null,
            PublicationEntitlementAction.Resume or
            PublicationEntitlementAction.UpdatePublishedContent or
            PublicationEntitlementAction.CreatorMediaUpload or
            PublicationEntitlementAction.RsvpSubmission or
            PublicationEntitlementAction.MemorySubmission or
            PublicationEntitlementAction.GiftReservation =>
                grant.AssignedInvitationId == context.InvitationId,
            _ => false
        };
    }

    private static bool RequiresCurrentPlanAdmission(PublicationEntitlementAction action) =>
        action is PublicationEntitlementAction.PublishNow or
            PublicationEntitlementAction.Schedule or
            PublicationEntitlementAction.Reschedule or
            PublicationEntitlementAction.Reactivate or
            PublicationEntitlementAction.CreatorMediaUpload;

    private static bool HasValidGrantShape(EntitlementGrantSnapshot grant)
    {
        if (grant.AccountId == Guid.Empty ||
            grant.GrantId == Guid.Empty ||
            grant.PlanId == Guid.Empty ||
            string.IsNullOrWhiteSpace(grant.PlanKey) ||
            !Enum.IsDefined(grant.PlanBillingKind) ||
            !Enum.IsDefined(grant.GrantSource) ||
            grant.RevokedAt < grant.GrantedAt)
        {
            return false;
        }

        var hasAssignment = grant.AssignedInvitationId is not null;
        var hasReservation = grant.ReservedAt is not null;

        var sourceMatchesPlan = grant.GrantSource switch
        {
            GrantSource.Free =>
                grant.PlanBillingKind == PlanBillingKind.Free &&
                string.Equals(grant.PlanKey, "free", StringComparison.Ordinal),
            GrantSource.IndividualPurchase =>
                grant.PlanBillingKind == PlanBillingKind.OneTime &&
                !string.Equals(grant.PlanKey, "free", StringComparison.Ordinal),
            GrantSource.OrganizationSubscription =>
                grant.PlanBillingKind == PlanBillingKind.Monthly &&
                !hasAssignment,
            _ => false
        };

        return grant.AssignedInvitationId != Guid.Empty &&
               hasAssignment == hasReservation &&
               (grant.ReservedAt is null || grant.ReservedAt >= grant.GrantedAt) &&
               (grant.ConsumedAt is null ||
                (grant.ReservedAt is not null && grant.ConsumedAt >= grant.ReservedAt)) &&
               sourceMatchesPlan;
    }

    private static bool TryReadAllValues(
        IReadOnlyCollection<PlanEntitlementValueSnapshot>? snapshots,
        out IReadOnlyDictionary<string, PlanEntitlementValueSnapshot> values)
    {
        values = new Dictionary<string, PlanEntitlementValueSnapshot>(StringComparer.Ordinal);
        if (snapshots is null || snapshots.Count != EntitlementCatalog.All.Count)
        {
            return false;
        }

        var result = new Dictionary<string, PlanEntitlementValueSnapshot>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            var definition = EntitlementCatalog.Find(snapshot.Key);
            if (definition is null || !result.TryAdd(definition.Key, snapshot))
            {
                return false;
            }

            if (definition.ValueType == EntitlementValueType.Numeric)
            {
                if (snapshot.BooleanValue is not null ||
                    snapshot.NumericValue is null or < 0 ||
                    snapshot.NumericValue > definition.HardCeiling)
                {
                    return false;
                }
            }
            else if (snapshot.NumericValue is not null || snapshot.BooleanValue is null)
            {
                return false;
            }
        }

        if (EntitlementCatalog.All.Any(definition => !result.ContainsKey(definition.Key)))
        {
            return false;
        }

        values = result;
        return true;
    }

    private static long Numeric(
        IReadOnlyDictionary<string, PlanEntitlementValueSnapshot> values,
        string key) => values[key].NumericValue!.Value;

    private static bool Boolean(
        IReadOnlyDictionary<string, PlanEntitlementValueSnapshot> values,
        string key) => values[key].BooleanValue!.Value;
}
