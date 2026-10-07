using Davetiye.Domain.Modules.Payments;
using Davetiye.Application.Modules.Payments.Contracts;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class OrganizationSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PaidThrough = Now.AddDays(24);

    [Fact]
    public void Cancellation_is_idempotent_and_keeps_access_until_the_paid_through_instant()
    {
        var subscription = CreateSubscription();

        Assert.True(subscription.CancelAtPeriodEndOn(Now));
        Assert.False(subscription.CancelAtPeriodEndOn(Now.AddMinutes(1)));
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now, subscription.CancelRequestedAtUtc);
        Assert.Equal(PaidThrough, subscription.CancellationBoundaryAtUtc);
        Assert.True(new OrganizationSubscriptionAccessSnapshot(
            subscription.Id, subscription.AccountId, subscription.PlanId, subscription.PaidThroughAtUtc,
            subscription.CancelAtPeriodEnd, subscription.CancelRequestedAtUtc, "Organization", 2499m, "TRY", "monthly",
            OrganizationSubscriptionAccessStatus.Active)
            .AllowsPublicAccessAt(PaidThrough.AddTicks(-1)));
        Assert.False(new OrganizationSubscriptionAccessSnapshot(
            subscription.Id, subscription.AccountId, subscription.PlanId, subscription.PaidThroughAtUtc,
            subscription.CancelAtPeriodEnd, subscription.CancelRequestedAtUtc, "Organization", 2499m, "TRY", "monthly",
            OrganizationSubscriptionAccessStatus.Active)
            .AllowsPublicAccessAt(PaidThrough));
    }

    [Fact]
    public void Cancellation_after_paid_through_does_not_change_state()
    {
        var subscription = CreateSubscription();

        Assert.False(subscription.CancelAtPeriodEndOn(PaidThrough));
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Null(subscription.CancelRequestedAtUtc);
    }

    [Fact]
    public void Immediate_deletion_cancellation_requests_provider_stop_idempotently_and_keeps_paid_through()
    {
        var subscription = CreateSubscription();

        Assert.True(subscription.RequestRenewalCancellationImmediately(Now));
        Assert.False(subscription.RequestRenewalCancellationImmediately(Now.AddMinutes(1)));
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(PaidThrough, subscription.PaidThroughAtUtc);
        Assert.Equal(Now, subscription.RenewalCancellationRequestedAtUtc);
        Assert.Null(subscription.RenewalCancellationCompletedAtUtc);
        Assert.True(subscription.CompleteRenewalCancellation(Now.AddMinutes(2)));
        Assert.False(subscription.CompleteRenewalCancellation(Now.AddMinutes(3)));
        Assert.Equal(Now.AddMinutes(2), subscription.RenewalCancellationCompletedAtUtc);
    }

    [Fact]
    public void Expiry_reminder_is_marked_once_for_the_current_paid_through_boundary()
    {
        var subscription = CreateSubscription();
        Assert.True(subscription.CancelAtPeriodEndOn(Now));

        Assert.True(subscription.ScheduleAccessExpiryReminderRetry(Now.AddMinutes(1)));
        Assert.Equal(Now.AddMinutes(1), subscription.AccessExpiryReminderRetryAfterUtc);
        Assert.True(subscription.MarkAccessExpiryReminderQueued(Now.AddDays(1)));
        Assert.Null(subscription.AccessExpiryReminderRetryAfterUtc);
        Assert.False(subscription.MarkAccessExpiryReminderQueued(Now.AddDays(2)));

        var nextPeriodEnd = PaidThrough.AddDays(31);
        var settled = subscription.ApplyRenewalSuccess("in-flight", PaidThrough, nextPeriodEnd, Now.AddDays(2));
        Assert.True(settled.SendSuccessNotice);
        Assert.Null(subscription.AccessExpiryReminderQueuedAtUtc);
        Assert.Null(subscription.AccessExpiryReminderRetryAfterUtc);
        Assert.True(subscription.MarkAccessExpiryReminderQueued(Now.AddDays(3)));
    }

    [Fact]
    public void Renewal_failure_notice_is_deduplicated_per_cycle_and_a_later_cycle_can_notify()
    {
        var subscription = CreateSubscription();
        var firstCycle = ("invoice-1", PaidThrough, PaidThrough.AddDays(31));
        var firstFailure = subscription.ApplyRenewalFailure(firstCycle.Item1, firstCycle.Item2, firstCycle.Item3, Now);
        var providerRetry = subscription.ApplyRenewalFailure(firstCycle.Item1, firstCycle.Item2, firstCycle.Item3, Now.AddHours(5));
        var recovered = subscription.ApplyRenewalSuccess(firstCycle.Item1, firstCycle.Item2, firstCycle.Item3, Now.AddHours(6));
        var nextCycle = subscription.ApplyRenewalFailure("invoice-2", firstCycle.Item3, firstCycle.Item3.AddDays(31), Now.AddDays(32));

        Assert.Equal(OrganizationSubscriptionTransitionKind.RenewalFailed, firstFailure.Kind);
        Assert.True(firstFailure.SendFailureNotice);
        Assert.False(providerRetry.SendFailureNotice);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Duplicate, providerRetry.Kind);
        Assert.True(recovered.SendSuccessNotice);
        Assert.True(nextCycle.SendFailureNotice);
        Assert.Equal(3, subscription.BillingCycles.Count);
    }

    [Fact]
    public void Successful_renewal_advances_paid_through_monotonically_and_sends_one_notice_per_cycle()
    {
        var subscription = CreateSubscription();
        var periodEnd = PaidThrough.AddDays(31);
        var failed = subscription.ApplyRenewalFailure("invoice-1", PaidThrough, periodEnd, Now);
        var settled = subscription.ApplyRenewalSuccess("invoice-1", PaidThrough, periodEnd, Now.AddHours(1));
        var duplicate = subscription.ApplyRenewalSuccess("invoice-1", PaidThrough, periodEnd, Now.AddHours(2));
        var delayedOlder = subscription.ApplyRenewalSuccess("older-invoice", Now, PaidThrough, Now.AddHours(3));

        Assert.True(failed.SendFailureNotice);
        Assert.True(settled.SendSuccessNotice);
        Assert.Equal(periodEnd, subscription.PaidThroughAtUtc);
        Assert.False(duplicate.SendSuccessNotice);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Duplicate, duplicate.Kind);
        Assert.False(delayedOlder.ChangedState);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Stale, delayedOlder.Kind);
        Assert.Equal(periodEnd, subscription.PaidThroughAtUtc);
    }

    [Fact]
    public void Cancellation_reconciles_one_in_flight_success_but_rejects_later_cycles()
    {
        var subscription = CreateSubscription();
        var nextPeriodEnd = PaidThrough.AddDays(31);
        subscription.CancelAtPeriodEndOn(Now);

        var racedPayment = subscription.ApplyRenewalSuccess("in-flight-invoice", PaidThrough, nextPeriodEnd, Now.AddSeconds(1));
        var duplicate = subscription.ApplyRenewalSuccess("in-flight-invoice", PaidThrough, nextPeriodEnd, Now.AddSeconds(2));
        var laterPeriod = subscription.ApplyRenewalSuccess("unexpected-later-invoice", nextPeriodEnd,
            nextPeriodEnd.AddDays(31), Now.AddDays(32));

        Assert.True(racedPayment.SendSuccessNotice);
        Assert.Equal(nextPeriodEnd, subscription.PaidThroughAtUtc);
        Assert.True(subscription.CancellationSettlementApplied);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Duplicate, duplicate.Kind);
        Assert.False(duplicate.SendSuccessNotice);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Canceled, laterPeriod.Kind);
        Assert.Equal(nextPeriodEnd, subscription.PaidThroughAtUtc);
    }

    [Fact]
    public void Previously_known_cycle_can_settle_after_cancellation_but_unknown_failure_is_rejected()
    {
        var subscription = CreateSubscription();
        var nextPeriodEnd = PaidThrough.AddDays(31);
        var firstFailure = subscription.ApplyRenewalFailure("known-in-flight", PaidThrough, nextPeriodEnd, Now);
        subscription.CancelAtPeriodEndOn(Now.AddMinutes(1));

        var settlement = subscription.ApplyRenewalSuccess("known-in-flight", PaidThrough, nextPeriodEnd, Now.AddMinutes(2));
        var unknownFailure = subscription.ApplyRenewalFailure("unknown-later", nextPeriodEnd,
            nextPeriodEnd.AddDays(31), Now.AddDays(32));

        Assert.True(firstFailure.SendFailureNotice);
        Assert.True(settlement.SendSuccessNotice);
        Assert.Equal(nextPeriodEnd, subscription.PaidThroughAtUtc);
        Assert.Equal(OrganizationSubscriptionTransitionKind.Canceled, unknownFailure.Kind);
        Assert.Equal(2, subscription.BillingCycles.Count);
    }

    [Fact]
    public void Immediate_deletion_cancellation_rejects_late_success_even_for_an_already_pending_cycle()
    {
        var subscription = CreateSubscription();
        var nextPeriodEnd = PaidThrough.AddDays(31);
        var pending = subscription.ApplyRenewalFailure("pending-renewal", PaidThrough, nextPeriodEnd, Now);
        Assert.True(pending.ChangedState);

        subscription.RequestRenewalCancellationImmediately(Now.AddMinutes(1));
        var lateSuccess = subscription.ApplyRenewalSuccess(
            "pending-renewal", PaidThrough, nextPeriodEnd, Now.AddMinutes(2));

        Assert.Equal(OrganizationSubscriptionTransitionKind.Canceled, lateSuccess.Kind);
        Assert.False(lateSuccess.ChangedState);
        Assert.Equal(PaidThrough, subscription.PaidThroughAtUtc);
        Assert.Null(subscription.RenewalCancellationCompletedAtUtc);
    }

    [Fact]
    public void Immediate_deletion_cancellation_at_paid_through_blocks_a_new_renewal_cycle()
    {
        var subscription = CreateSubscription();
        Assert.True(subscription.RequestRenewalCancellationImmediately(PaidThrough));
        Assert.False(subscription.CancelAtPeriodEnd); // Paid-through has elapsed, but provider intent remains durable.

        var lateSuccess = subscription.ApplyRenewalSuccess(
            "after-deletion", PaidThrough, PaidThrough.AddDays(31), PaidThrough.AddMinutes(1));

        Assert.Equal(OrganizationSubscriptionTransitionKind.Canceled, lateSuccess.Kind);
        Assert.Equal(PaidThrough, subscription.PaidThroughAtUtc);
        Assert.Null(subscription.RenewalCancellationCompletedAtUtc);
    }

    [Fact]
    public void Provider_cycle_identity_cannot_be_reused_for_different_dates()
    {
        var subscription = CreateSubscription();
        var periodEnd = PaidThrough.AddDays(31);
        subscription.ApplyRenewalFailure("invoice-reuse", PaidThrough, periodEnd, Now);

        Assert.Throws<InvalidOperationException>(() => subscription.ApplyRenewalSuccess(
            "invoice-reuse", PaidThrough.AddDays(1), periodEnd.AddDays(1), Now.AddHours(1)));
    }

    [Fact]
    public void Initial_activation_uses_the_verified_first_period_and_records_its_cycle()
    {
        var periodEnd = PaidThrough.AddDays(31);
        var subscription = OrganizationSubscription.ActivateFromVerifiedInitialPayment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2499m, "iyzico", "provider-subscription-2",
            "initial-invoice", Now, periodEnd, Now.AddMinutes(1));

        Assert.Equal(periodEnd, subscription.PaidThroughAtUtc);
        Assert.Equal(2499m, subscription.PriceAmountAtActivation);
        var cycle = Assert.Single(subscription.BillingCycles);
        Assert.Equal("initial-invoice", cycle.ProviderCycleId);
        Assert.Equal(OrganizationSubscriptionBillingCycleStatus.Succeeded, cycle.Status);
        Assert.Null(cycle.SuccessNoticeRecordedAtUtc);
    }

    [Fact]
    public void Initial_activation_timestamp_must_fall_inside_the_verified_period()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrganizationSubscription.ActivateFromVerifiedInitialPayment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2499m, "iyzico", "provider-subscription-3",
            "initial-invoice-outside", Now, PaidThrough, PaidThrough));
    }

    [Fact]
    public void Cycle_bounds_must_be_utc_even_when_cycle_identity_already_exists()
    {
        var subscription = CreateSubscription();
        var periodEnd = PaidThrough.AddDays(31);
        subscription.ApplyRenewalFailure("invoice-utc", PaidThrough, periodEnd, Now);

        var localOffsetStart = PaidThrough.ToOffset(TimeSpan.FromHours(3));
        var localOffsetEnd = periodEnd.ToOffset(TimeSpan.FromHours(3));
        Assert.Throws<ArgumentException>(() => subscription.ApplyRenewalSuccess(
            "invoice-utc", localOffsetStart, localOffsetEnd, Now.AddHours(1)));
    }

    private static OrganizationSubscription CreateSubscription() => OrganizationSubscription.ActivateFromVerifiedInitialPayment(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2499m, "iyzico", "provider-subscription-1", "initial-invoice",
        Now.AddDays(-1), PaidThrough, Now);
}
