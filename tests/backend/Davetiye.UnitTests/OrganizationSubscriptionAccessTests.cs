using Davetiye.Application.Modules.Payments;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.SharedKernel;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class OrganizationSubscriptionAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Effective_resolver_allows_organization_grant_before_paid_through_and_denies_at_boundary()
    {
        var grant = CreateOrganizationGrant(Now.AddMinutes(1));
        var resolver = new EffectiveEntitlementResolver(new FixedGrantReader(grant), new FixedClock(Now));
        var context = new EntitlementResolutionContext(grant.AccountId, grant.GrantId, Guid.NewGuid(),
            PublicationEntitlementAction.Resume);

        var allowed = await resolver.ResolveAsync(context, CancellationToken.None);
        var boundaryResolver = new EffectiveEntitlementResolver(
            new FixedGrantReader(grant with { OrganizationSubscriptionPaidThroughAtUtc = Now }), new FixedClock(Now));
        var denied = await boundaryResolver.ResolveAsync(context, CancellationToken.None);

        Assert.True(allowed.IsGranted);
        Assert.Equal(EntitlementResolutionDenial.GrantNotEffective, denied.Denial);
    }

    [Fact]
    public void Public_grant_gate_keeps_organization_access_until_paid_through_and_cuts_at_boundary()
    {
        var accountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var starts = Now.AddDays(-1);
        var grant = new PublicationGrantAccessSnapshot(grantId, accountId, true, null,
            starts, null, null, null, GrantSource.OrganizationSubscription, Now.AddMinutes(1));
        var boundary = new PublicationGrantAccessSnapshot(grantId, accountId, true, null,
            starts, null, null, null, GrantSource.OrganizationSubscription, Now);

        Assert.True(PublicationGrantAccessPolicy.IsAllowed(grant, accountId, invitationId, grantId, starts, Now));
        Assert.False(PublicationGrantAccessPolicy.IsAllowed(boundary, accountId, invitationId, grantId, starts, Now));
    }

    [Fact]
    public async Task Lifecycle_service_uses_local_clock_for_activation_and_cancellation()
    {
        var store = new RecordingStore();
        var service = new OrganizationSubscriptionLifecycleService(store, new FixedClock(Now));
        var activation = new VerifiedOrganizationSubscriptionActivation(Guid.NewGuid(), Guid.NewGuid(), "provider",
            "subscription", "initial-cycle", Now, Now.AddDays(30), 2499m, PurchasableBillingKind.Monthly);
        var renewal = new VerifiedOrganizationSubscriptionRenewal("provider", "subscription", "cycle-2",
            Now.AddDays(30), Now.AddDays(61), OrganizationSubscriptionRenewalOutcome.Succeeded);

        await service.ActivateFromVerifiedInitialPaymentAsync(activation, CancellationToken.None);
        await service.ApplyVerifiedRenewalAsync(renewal, CancellationToken.None);
        await service.CancelAtPeriodEndAsync(activation.AccountId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(Now, store.ActivationAppliedAtUtc);
        Assert.Equal(Now, store.RenewalAppliedAtUtc);
        Assert.Equal(Now, store.CancellationRequestedAtUtc);
    }

    private static EntitlementGrantSnapshot CreateOrganizationGrant(DateTimeOffset paidThroughAtUtc)
    {
        var values = EntitlementCatalog.All.Select(definition => definition.ValueType == EntitlementValueType.Numeric
            ? new PlanEntitlementValueSnapshot(definition.Key,
                definition.Key == EntitlementCatalog.MaxPublishDays ? 30 : 1, null)
            : new PlanEntitlementValueSnapshot(definition.Key, null, true)).ToArray();
        return new EntitlementGrantSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "organization", true,
            PlanBillingKind.Monthly, GrantSource.OrganizationSubscription, Now.AddDays(-1), null, null, null, null,
            values, paidThroughAtUtc);
    }

    private sealed class FixedGrantReader(EntitlementGrantSnapshot snapshot) : IEntitlementGrantReader
    {
        public Task<EntitlementGrantSnapshot?> FindOwnedGrantAsync(Guid accountId, Guid grantId,
            CancellationToken cancellationToken) => Task.FromResult<EntitlementGrantSnapshot?>(snapshot);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class RecordingStore : IOrganizationSubscriptionLifecycleStore
    {
        public DateTimeOffset? ActivationAppliedAtUtc { get; private set; }
        public DateTimeOffset? RenewalAppliedAtUtc { get; private set; }
        public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }

        public Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(Guid accountId,
            DateTimeOffset evaluatedAtUtc, CancellationToken cancellationToken) => Task.FromResult<OrganizationSubscriptionAccessSnapshot?>(null);

        public Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
            VerifiedOrganizationSubscriptionActivation activation, DateTimeOffset appliedAtUtc,
            CancellationToken cancellationToken)
        {
            ActivationAppliedAtUtc = appliedAtUtc;
            return Task.FromResult(new OrganizationSubscriptionCommandResult(
                OrganizationSubscriptionCommandOutcome.Applied, false, false, false, activation.FirstPeriodEndsAtUtc));
        }

        public Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
            VerifiedOrganizationSubscriptionRenewal renewal, DateTimeOffset appliedAtUtc,
            CancellationToken cancellationToken)
        {
            RenewalAppliedAtUtc = appliedAtUtc;
            return Task.FromResult(new OrganizationSubscriptionCommandResult(
                OrganizationSubscriptionCommandOutcome.Applied, false, true, false, renewal.BillingPeriodEndsAtUtc));
        }

        public Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(Guid accountId, Guid subscriptionId,
            DateTimeOffset requestedAtUtc, CancellationToken cancellationToken)
        {
            CancellationRequestedAtUtc = requestedAtUtc;
            return Task.FromResult(new OrganizationSubscriptionCommandResult(
                OrganizationSubscriptionCommandOutcome.Applied, false, false, true, null));
        }
    }
}
