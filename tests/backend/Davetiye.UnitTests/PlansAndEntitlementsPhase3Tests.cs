using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Domain.Modules.SharedKernel;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class PlansAndEntitlementsPhase3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_validates_commercial_terms_and_increments_revision_only_for_changes()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), "standard", "Standard", true, 699m, "try", PlanBillingKind.OneTime);

        Assert.Equal("TRY", plan.Currency);
        Assert.Equal(0, plan.Revision);

        plan.UpdateCommercialTerms(699m, "TRY", PlanBillingKind.OneTime);
        Assert.Equal(0, plan.Revision);

        plan.UpdateCommercialTerms(749m, "TRY", PlanBillingKind.OneTime);
        Assert.Equal(1, plan.Revision);

        Assert.Throws<ArgumentException>(() => Plan.Create(
            Guid.NewGuid(), "free", "Free", true, 1m, "TRY", PlanBillingKind.Free));
    }

    [Fact]
    public void Plan_description_is_optional_trimmed_bounded_and_revisioned_only_when_changed()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), "standard", "Standard", true, 699m, "TRY", PlanBillingKind.OneTime,
            description: "  A short description  ");

        Assert.Equal("A short description", plan.Description);
        Assert.Equal(0, plan.Revision);

        plan.UpdateDescription(" A short description ");
        Assert.Equal(0, plan.Revision);

        plan.UpdateDescription("  ");
        Assert.Null(plan.Description);
        Assert.Equal(1, plan.Revision);

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.UpdateDescription(
            new string('x', Plan.DescriptionMaxLength + 1)));
    }

    [Fact]
    public void Admin_plan_metadata_validates_fields_and_free_requires_zero_price()
    {
        var plan = Plan.Create(Guid.NewGuid(), "standard", "Standard", true, 699m, "TRY", PlanBillingKind.OneTime);
        plan.UpdateAdminMetadata("  Standard Plus ", "  More features ", 799.99m, PlanBillingKind.Monthly);
        Assert.Equal("Standard Plus", plan.DisplayName);
        Assert.Equal("More features", plan.Description);
        Assert.Equal(799.99m, plan.PriceAmount);
        Assert.Equal(PlanBillingKind.Monthly, plan.BillingKind);
        Assert.Equal(1, plan.Revision);
        Assert.Throws<ArgumentException>(() => plan.UpdateAdminMetadata("Standard", null, 1m, PlanBillingKind.Free));
        Assert.Throws<ArgumentException>(() => plan.UpdateAdminMetadata(" ", null, 0m, PlanBillingKind.Monthly));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.UpdateAdminMetadata("Standard", null, 1.00001m, PlanBillingKind.Monthly));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.UpdateAdminMetadata("Standard", null, 0m, PlanBillingKind.Monthly));
        // Historical zero-priced paid rows remain loadable; the Admin command requires positive paid pricing.
        Assert.Equal(0m, Plan.Create(Guid.NewGuid(), "legacy", "Legacy", true, 0m, "TRY", PlanBillingKind.OneTime).PriceAmount);
    }

    [Theory]
    [InlineData(GrantSource.Free, PlanBillingKind.Free)]
    [InlineData(GrantSource.IndividualPurchase, PlanBillingKind.OneTime)]
    [InlineData(GrantSource.OrganizationSubscription, PlanBillingKind.Monthly)]
    public void Grant_billing_shape_is_snapshotted_from_immutable_source(GrantSource source, PlanBillingKind kind)
    {
        Assert.Equal(kind, CreateGrant(source).BillingKindAtGrant);
    }

    [Fact]
    public void Free_grant_reservation_can_be_released_before_consumption_and_reused()
    {
        var grant = CreateGrant(GrantSource.Free);
        var firstInvitation = Guid.NewGuid();
        var secondInvitation = Guid.NewGuid();

        grant.ReserveForInvitation(firstInvitation, Now);
        grant.ReleaseReservation(firstInvitation);
        grant.ReserveForInvitation(secondInvitation, Now.AddMinutes(1));

        Assert.Equal(secondInvitation, grant.AssignedInvitationId);
        Assert.Null(grant.ConsumedAt);
        Assert.Equal(3, grant.Revision);
    }

    [Fact]
    public void Consumed_free_grant_stays_assigned_and_cannot_be_released_or_reassigned()
    {
        var grant = CreateGrant(GrantSource.Free);
        var invitationId = Guid.NewGuid();

        grant.ReserveForInvitation(invitationId, Now);
        grant.ConsumeForInvitation(invitationId, Now.AddHours(1));
        grant.ConsumeForInvitation(invitationId, Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), grant.ConsumedAt);
        Assert.Equal(2, grant.Revision);
        Assert.Throws<InvalidOperationException>(() => grant.ReleaseReservation(invitationId));
        Assert.Throws<InvalidOperationException>(() =>
            grant.ReserveForInvitation(Guid.NewGuid(), Now.AddHours(2)));
    }

    [Fact]
    public void Immediate_consumption_reserves_and_consumes_in_one_transition()
    {
        var grant = CreateGrant(GrantSource.IndividualPurchase);
        var invitationId = Guid.NewGuid();

        grant.ConsumeForInvitation(invitationId, Now);

        Assert.Equal(invitationId, grant.AssignedInvitationId);
        Assert.Equal(Now, grant.ReservedAt);
        Assert.Equal(Now, grant.ConsumedAt);
        Assert.Equal(1, grant.Revision);
    }

    [Fact]
    public void Organization_grant_cannot_be_assigned_to_one_invitation()
    {
        var grant = CreateGrant(GrantSource.OrganizationSubscription);

        Assert.Throws<InvalidOperationException>(() =>
            grant.ReserveForInvitation(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Quota_policy_uses_half_open_intervals_across_the_whole_account()
    {
        var requestedInvitationId = Guid.NewGuid();
        var requested = new PublicationInterval(Now, Now.AddDays(1));
        var slots = new[]
        {
            new AccountPublicationSlot(Guid.NewGuid(),
                new PublicationInterval(Now.AddDays(-1), Now)),
            new AccountPublicationSlot(Guid.NewGuid(),
                new PublicationInterval(Now.AddHours(1), Now.AddDays(2))),
            new AccountPublicationSlot(requestedInvitationId,
                new PublicationInterval(Now, Now.AddDays(1)))
        };

        var decision = PublicationQuotaPolicy.Evaluate(
            requestedInvitationId, requested, 1, 1, slots);

        Assert.False(decision.IsAllowed);
        Assert.Equal(PublicationQuotaDenial.ActiveInvitationQuotaExceeded, decision.Denial);
        Assert.Equal(1, decision.OverlappingInvitationCount);
    }

    [Fact]
    public void Quota_policy_allows_a_candidate_spanning_adjacent_or_disjoint_reservations()
    {
        var slots = new[]
        {
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now, Now.AddDays(1))),
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(1), Now.AddDays(2))),
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(3), Now.AddDays(4)))
        };

        var decision = PublicationQuotaPolicy.Evaluate(
            Guid.NewGuid(), new PublicationInterval(Now, Now.AddDays(4)), 4, 2, slots);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.OverlappingInvitationCount);
    }

    [Fact]
    public void Quota_policy_rejects_a_peak_overlap_inside_the_candidate_window()
    {
        var slots = new[]
        {
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now, Now.AddDays(2))),
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(1), Now.AddDays(3)))
        };

        var decision = PublicationQuotaPolicy.Evaluate(
            Guid.NewGuid(), new PublicationInterval(Now, Now.AddDays(3)), 3, 2, slots);

        Assert.False(decision.IsAllowed);
        Assert.Equal(PublicationQuotaDenial.ActiveInvitationQuotaExceeded, decision.Denial);
        Assert.Equal(2, decision.OverlappingInvitationCount);
    }

    [Fact]
    public void Quota_policy_counts_duplicate_and_overlapping_rows_of_one_invitation_once()
    {
        var existingId = Guid.NewGuid();
        var requestedId = Guid.NewGuid();
        var slots = new[]
        {
            new AccountPublicationSlot(existingId, new PublicationInterval(Now.AddDays(-1), Now.AddDays(1))),
            new AccountPublicationSlot(existingId, new PublicationInterval(Now.AddDays(-1), Now.AddDays(1))),
            new AccountPublicationSlot(existingId, new PublicationInterval(Now, Now.AddDays(2))),
            new AccountPublicationSlot(existingId, new PublicationInterval(Now.AddDays(2), Now.AddDays(4))),
            new AccountPublicationSlot(requestedId, new PublicationInterval(Now, Now.AddDays(3)))
        };

        var decision = PublicationQuotaPolicy.Evaluate(
            requestedId, new PublicationInterval(Now, Now.AddDays(3)), 3, 2, slots);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.OverlappingInvitationCount);
    }

    [Fact]
    public void Quota_policy_measures_peak_only_inside_requested_interval_and_respects_zero_limit()
    {
        var requested = new PublicationInterval(Now, Now.AddDays(1));
        var slots = new[]
        {
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(-2), Now.AddHours(1))),
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(-1), Now)),
            new AccountPublicationSlot(Guid.NewGuid(), new PublicationInterval(Now.AddDays(1), Now.AddDays(2)))
        };

        Assert.True(PublicationQuotaPolicy.Evaluate(Guid.NewGuid(), requested, 1, 2, slots).IsAllowed);
        Assert.False(PublicationQuotaPolicy.Evaluate(Guid.NewGuid(), requested, 1, 0, []).IsAllowed);
    }

    [Fact]
    public void Quota_policy_rejects_duration_over_current_entitlement()
    {
        var decision = PublicationQuotaPolicy.Evaluate(
            Guid.NewGuid(),
            new PublicationInterval(Now, Now.AddDays(2)),
            maxPublishDays: 1,
            maxActiveInvitations: 10,
            existingSlots: []);

        Assert.Equal(PublicationQuotaDenial.PublishDurationExceeded, decision.Denial);
    }

    [Fact]
    public async Task Resolver_returns_one_complete_typed_snapshot_without_merging()
    {
        var grant = CompleteSnapshot();
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(grant), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                Guid.NewGuid(),
                PublicationEntitlementAction.Schedule),
            CancellationToken.None);

        Assert.True(result.IsGranted);
        Assert.Equal(PlanBillingKind.OneTime, result.Entitlements!.PlanBillingKind);
        Assert.Equal(30, result.Entitlements.MaxPublishDays);
        Assert.Equal(1, result.Entitlements.MaxActiveInvitations);
        Assert.True(result.Entitlements.MemoriesEnabled);
    }

    [Fact]
    public async Task Resolver_fails_closed_when_a_supported_value_is_missing_or_invalid()
    {
        var grant = CompleteSnapshot();
        var invalid = grant with { Values = grant.Values.Skip(1).ToArray() };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(invalid), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                Guid.NewGuid(),
                PublicationEntitlementAction.PublishNow),
            CancellationToken.None);

        Assert.False(result.IsGranted);
        Assert.Equal(EntitlementResolutionDenial.InvalidConfiguration, result.Denial);
    }

    [Fact]
    public async Task Resolver_fails_closed_when_a_free_source_points_to_a_non_free_plan()
    {
        var invalid = CompleteSnapshot() with
        {
            GrantSource = GrantSource.Free,
            PlanKey = "premium"
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(invalid), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                invalid.AccountId,
                invalid.GrantId,
                Guid.NewGuid(),
                PublicationEntitlementAction.PublishNow),
            CancellationToken.None);

        Assert.Equal(EntitlementResolutionDenial.InvalidConfiguration, result.Denial);
    }

    [Theory]
    [InlineData(GrantSource.Free, "free", PlanBillingKind.OneTime)]
    [InlineData(GrantSource.IndividualPurchase, "standard", PlanBillingKind.Monthly)]
    [InlineData(GrantSource.IndividualPurchase, "free", PlanBillingKind.OneTime)]
    [InlineData(GrantSource.OrganizationSubscription, "organization", PlanBillingKind.OneTime)]
    public async Task Resolver_fails_closed_when_grant_source_does_not_match_plan_shape(
        GrantSource source,
        string planKey,
        PlanBillingKind billingKind)
    {
        var invalid = CompleteSnapshot() with
        {
            GrantSource = source,
            PlanKey = planKey,
            PlanBillingKind = billingKind
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(invalid), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                invalid.AccountId,
                invalid.GrantId,
                Guid.NewGuid(),
                PublicationEntitlementAction.PublishNow),
            CancellationToken.None);

        Assert.Equal(EntitlementResolutionDenial.InvalidConfiguration, result.Denial);
    }

    [Theory]
    [InlineData(GrantSource.Free, "free", PlanBillingKind.Free)]
    [InlineData(GrantSource.IndividualPurchase, "standard", PlanBillingKind.OneTime)]
    [InlineData(GrantSource.OrganizationSubscription, "organization", PlanBillingKind.Monthly)]
    public async Task Resolver_accepts_each_supported_source_plan_mapping(
        GrantSource source,
        string planKey,
        PlanBillingKind billingKind)
    {
        var grant = CompleteSnapshot() with
        {
            GrantSource = source,
            PlanKey = planKey,
            PlanBillingKind = billingKind
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(grant), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                Guid.NewGuid(),
                PublicationEntitlementAction.PublishNow),
            CancellationToken.None);

        Assert.True(result.IsGranted);
    }

    [Fact]
    public async Task Resolver_fails_closed_when_organization_grant_is_invitation_assigned()
    {
        var invitationId = Guid.NewGuid();
        var invalid = CompleteSnapshot() with
        {
            GrantSource = GrantSource.OrganizationSubscription,
            PlanKey = "organization",
            PlanBillingKind = PlanBillingKind.Monthly,
            AssignedInvitationId = invitationId,
            ReservedAt = Now.AddHours(-1)
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(invalid), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                invalid.AccountId,
                invalid.GrantId,
                invitationId,
                PublicationEntitlementAction.PublishNow),
            CancellationToken.None);

        Assert.Equal(EntitlementResolutionDenial.InvalidConfiguration, result.Denial);
    }

    [Fact]
    public async Task Resolver_does_not_allow_a_consumed_individual_grant_for_reactivation()
    {
        var invitationId = Guid.NewGuid();
        var grant = CompleteSnapshot() with
        {
            AssignedInvitationId = invitationId,
            ReservedAt = Now.AddHours(-12),
            ConsumedAt = Now.AddHours(-6)
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(grant), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                invitationId,
                PublicationEntitlementAction.Reactivate),
            CancellationToken.None);

        Assert.Equal(EntitlementResolutionDenial.ActionNotAllowedByGrantState, result.Denial);
    }

    [Theory]
    [InlineData(PublicationEntitlementAction.PublishNow)]
    [InlineData(PublicationEntitlementAction.Schedule)]
    [InlineData(PublicationEntitlementAction.Reschedule)]
    [InlineData(PublicationEntitlementAction.Reactivate)]
    [InlineData(PublicationEntitlementAction.CreatorMediaUpload)]
    public async Task Resolver_rejects_new_admission_when_plan_is_inactive(
        PublicationEntitlementAction action)
    {
        var grant = CompleteSnapshot() with { PlanIsActive = false };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(grant), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                Guid.NewGuid(),
                action),
            CancellationToken.None);

        Assert.Equal(EntitlementResolutionDenial.GrantNotEffective, result.Denial);
    }

    [Theory]
    [InlineData(PublicationEntitlementAction.Resume)]
    [InlineData(PublicationEntitlementAction.UpdatePublishedContent)]
    public async Task Resolver_allows_existing_window_actions_after_plan_deactivation(
        PublicationEntitlementAction action)
    {
        var invitationId = Guid.NewGuid();
        var grant = CompleteSnapshot() with
        {
            PlanIsActive = false,
            AssignedInvitationId = invitationId,
            ReservedAt = Now.AddDays(-1),
            ConsumedAt = Now.AddHours(-12)
        };
        var resolver = new EffectiveEntitlementResolver(new StubGrantReader(grant), new StubClock(Now));

        var result = await resolver.ResolveAsync(
            new EntitlementResolutionContext(
                grant.AccountId,
                grant.GrantId,
                invitationId,
                action),
            CancellationToken.None);

        Assert.True(result.IsGranted);
    }

    [Fact]
    public async Task Free_service_validates_account_reference_before_mutating_store()
    {
        var store = new StubFreeGrantStore();
        var service = new FreePublicationGrantService(
            new StubAccountValidator(AccountReferenceStatus.NotFound),
            new StubInvitationOwnershipValidator(isOwned: true),
            new StubAccountQuotaTransactionRunner(),
            store);

        var result = await service.ReserveAsync(
            Guid.NewGuid(), Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.AccountNotFound, result.Outcome);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Free_service_rejects_unverified_account_before_mutating_store()
    {
        var store = new StubFreeGrantStore();
        var service = new FreePublicationGrantService(
            new StubAccountValidator(AccountReferenceStatus.Unverified),
            new StubInvitationOwnershipValidator(isOwned: true),
            new StubAccountQuotaTransactionRunner(),
            store);

        var result = await service.ConsumeImmediatelyAsync(
            Guid.NewGuid(), Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.AccountNotVerified, result.Outcome);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Free_service_rejects_banned_account_as_inactive_before_checking_ownership()
    {
        var store = new StubFreeGrantStore();
        var ownership = new StubInvitationOwnershipValidator(isOwned: true);
        var service = new FreePublicationGrantService(
            new StubAccountValidator(AccountReferenceStatus.Banned),
            ownership,
            new StubAccountQuotaTransactionRunner(),
            store);

        var result = await service.ReserveAsync(
            Guid.NewGuid(), Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.AccountInactive, result.Outcome);
        Assert.Equal(0, ownership.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Free_service_rejects_missing_or_foreign_invitation_before_mutating_store()
    {
        var store = new StubFreeGrantStore();
        var ownership = new StubInvitationOwnershipValidator(isOwned: false);
        var service = new FreePublicationGrantService(
            new StubAccountValidator(AccountReferenceStatus.Verified),
            ownership,
            new StubAccountQuotaTransactionRunner(),
            store);

        var accountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var results = new[]
        {
            await service.ReserveAsync(accountId, invitationId, Now, CancellationToken.None),
            await service.ConsumeImmediatelyAsync(accountId, invitationId, Now, CancellationToken.None),
            await service.ConsumeReservedAtStartAsync(accountId, invitationId, Now, CancellationToken.None)
        };

        Assert.All(results, result => Assert.Equal(
            FreeGrantReservationOutcome.InvitationNotFoundOrNotOwned,
            result.Outcome));
        Assert.Equal(3, ownership.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Free_service_mutates_store_only_after_account_and_invitation_are_authorized()
    {
        var store = new StubFreeGrantStore();
        var transactionRunner = new StubAccountQuotaTransactionRunner();
        var service = new FreePublicationGrantService(
            new StubAccountValidator(AccountReferenceStatus.Verified),
            new StubInvitationOwnershipValidator(isOwned: true),
            transactionRunner,
            store);

        var result = await service.ConsumeReservedAtStartAsync(
            Guid.NewGuid(), Guid.NewGuid(), Now, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, transactionRunner.CallCount);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public void Entitlement_and_setting_mutations_advance_revision_only_when_changed()
    {
        var entitlement = PlanEntitlement.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntitlementCatalog.MaxImages, 5, null);
        var setting = SystemSetting.Create(
            Guid.NewGuid(), "example", SystemSettingValueType.Integer, "3");

        entitlement.UpdateValue(5, null);
        setting.UpdateValue("3");
        Assert.Equal(0, entitlement.Revision);
        Assert.Equal(0, setting.Revision);

        entitlement.UpdateValue(4, null);
        setting.UpdateValue("4");
        Assert.Equal(1, entitlement.Revision);
        Assert.Equal(1, setting.Revision);
    }

    private static AccountPlanGrant CreateGrant(GrantSource source) =>
        AccountPlanGrant.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source, Now.AddDays(-1));

    private static EntitlementGrantSnapshot CompleteSnapshot()
    {
        var values = EntitlementCatalog.All
            .Select(definition => definition.ValueType == EntitlementValueType.Numeric
                ? new PlanEntitlementValueSnapshot(
                    definition.Key,
                    definition.Key == EntitlementCatalog.MaxPublishDays ? 30 : 1,
                    null)
                : new PlanEntitlementValueSnapshot(definition.Key, null, true))
            .ToArray();

        return new EntitlementGrantSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "standard",
            PlanIsActive: true,
            PlanBillingKind.OneTime,
            GrantSource.IndividualPurchase,
            Now.AddDays(-1),
            RevokedAt: null,
            AssignedInvitationId: null,
            ReservedAt: null,
            ConsumedAt: null,
            values,
            OrganizationSubscriptionPaidThroughAtUtc: Now.AddDays(1));
    }

    private sealed class StubGrantReader(EntitlementGrantSnapshot? snapshot) : IEntitlementGrantReader
    {
        public Task<EntitlementGrantSnapshot?> FindOwnedGrantAsync(
            Guid accountId,
            Guid grantId,
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class StubAccountValidator(AccountReferenceStatus status) : IAccountReferenceValidator
    {
        public Task<AccountReferenceStatus> GetStatusAsync(
            Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private sealed class StubInvitationOwnershipValidator(bool isOwned)
        : IInvitationOwnershipValidator
    {
        public int CallCount { get; private set; }

        public Task<bool> IsOwnedByAccountAsync(
            Guid accountId,
            Guid invitationId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(isOwned);
        }
    }

    private sealed class StubAccountQuotaTransactionRunner : IAccountQuotaTransactionRunner
    {
        public int CallCount { get; private set; }

        public async Task<TResult> ExecuteAsync<TResult>(
            Guid accountId,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return await operation(cancellationToken);
        }
    }

    private sealed class StubFreeGrantStore : IFreeGrantReservationStore
    {
        public int CallCount { get; private set; }

        public Task<FreeGrantReservationResult> TryReserveAsync(
            Guid accountId,
            Guid invitationId,
            DateTimeOffset reservedAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new FreeGrantReservationResult(
                FreeGrantReservationOutcome.Reserved,
                Guid.NewGuid()));
        }

        public Task<FreeGrantReservationResult> TryConsumeAsync(
            Guid accountId,
            Guid invitationId,
            DateTimeOffset consumedAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new FreeGrantReservationResult(
                FreeGrantReservationOutcome.Reserved,
                Guid.NewGuid()));
        }

        public Task<FreeGrantReservationResult> TryConsumeExistingReservationAsync(
            Guid accountId,
            Guid invitationId,
            DateTimeOffset consumedAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new FreeGrantReservationResult(
                FreeGrantReservationOutcome.Reserved,
                Guid.NewGuid()));
        }

    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
