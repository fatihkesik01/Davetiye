using Davetiye.Domain.Modules.PlansAndEntitlements;

namespace Davetiye.Domain.Modules.Payments;

/// <summary>
/// Payments-owned recurring Organization entitlement state. Paid-through expiry is evaluated by
/// readers at the requested instant; no background job is required to end public access.
/// </summary>
public sealed class OrganizationSubscription
{
    private readonly List<OrganizationSubscriptionBillingCycle> _billingCycles = [];

    private OrganizationSubscription() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid PlanId { get; private set; }
    public decimal PriceAmountAtActivation { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public string ProviderSubscriptionId { get; private set; } = string.Empty;
    public DateTimeOffset PaidThroughAtUtc { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset? CancelRequestedAtUtc { get; private set; }
    public DateTimeOffset? CancellationBoundaryAtUtc { get; private set; }
    public bool CancellationSettlementApplied { get; private set; }
    public DateTimeOffset? RenewalCancellationRequestedAtUtc { get; private set; }
    public DateTimeOffset? RenewalCancellationCompletedAtUtc { get; private set; }
    public DateTimeOffset? AccessExpiryReminderQueuedAtUtc { get; private set; }
    public DateTimeOffset? AccessExpiryReminderRetryAfterUtc { get; private set; }
    public IReadOnlyCollection<OrganizationSubscriptionBillingCycle> BillingCycles => _billingCycles.AsReadOnly();

    private static OrganizationSubscription Create(
        Guid id,
        Guid accountId,
        Guid planId,
        decimal priceAmountAtActivation,
        string providerName,
        string providerSubscriptionId,
        DateTimeOffset paidThroughAtUtc)
    {
        if (id == Guid.Empty || accountId == Guid.Empty || planId == Guid.Empty)
            throw new ArgumentException("Subscription, account and plan identifiers must not be empty.");
        if (string.IsNullOrWhiteSpace(providerName) || providerName.Length > 64)
            throw new ArgumentException("A provider name is required.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(providerSubscriptionId) || providerSubscriptionId.Length > 128)
            throw new ArgumentException("A provider subscription identity is required.", nameof(providerSubscriptionId));
        EnsureUtc(paidThroughAtUtc, nameof(paidThroughAtUtc));
        ValidatePrice(priceAmountAtActivation);

        return new OrganizationSubscription
        {
            Id = id,
            AccountId = accountId,
            PlanId = planId,
            PriceAmountAtActivation = priceAmountAtActivation,
            ProviderName = providerName,
            ProviderSubscriptionId = providerSubscriptionId,
            PaidThroughAtUtc = paidThroughAtUtc
        };
    }

    /// <summary>Creates the subscription only from an already verified initial provider payment.</summary>
    public static OrganizationSubscription ActivateFromVerifiedInitialPayment(
        Guid id,
        Guid accountId,
        Guid planId,
        decimal priceAmountAtActivation,
        string providerName,
        string providerSubscriptionId,
        string providerCycleId,
        DateTimeOffset firstPeriodStartsAtUtc,
        DateTimeOffset firstPeriodEndsAtUtc,
        DateTimeOffset appliedAtUtc)
    {
        ValidateCyclePeriod(firstPeriodStartsAtUtc, firstPeriodEndsAtUtc);
        EnsureUtc(appliedAtUtc, nameof(appliedAtUtc));
        if (appliedAtUtc < firstPeriodStartsAtUtc || appliedAtUtc >= firstPeriodEndsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(appliedAtUtc), "Initial activation must occur within the verified first billing period.");
        var subscription = Create(id, accountId, planId, priceAmountAtActivation, providerName, providerSubscriptionId, firstPeriodStartsAtUtc);
        var initialCycle = OrganizationSubscriptionBillingCycle.Create(
            Guid.NewGuid(), id, providerCycleId, firstPeriodStartsAtUtc, firstPeriodEndsAtUtc);
        initialCycle.RecordInitialActivation(appliedAtUtc);
        subscription._billingCycles.Add(initialCycle);
        subscription.PaidThroughAtUtc = firstPeriodEndsAtUtc;
        return subscription;
    }

    /// <summary>Schedules non-renewal at the current paid-through instant. Replays are harmless.</summary>
    public bool CancelAtPeriodEndOn(DateTimeOffset requestedAtUtc)
    {
        EnsureUtc(requestedAtUtc, nameof(requestedAtUtc));
        if (CancelAtPeriodEnd || requestedAtUtc >= PaidThroughAtUtc)
            return false;

        CancelAtPeriodEnd = true;
        CancelRequestedAtUtc = requestedAtUtc;
        CancellationBoundaryAtUtc = PaidThroughAtUtc;
        return true;
    }

    /// <summary>
    /// Records the immediate provider auto-renewal cancellation requested by verified account
    /// deletion. Local renewal processing is stopped immediately while paid-through state remains
    /// unchanged; the provider completion timestamp is written only after provider confirmation.
    /// </summary>
    public bool RequestRenewalCancellationImmediately(DateTimeOffset requestedAtUtc)
    {
        EnsureUtc(requestedAtUtc, nameof(requestedAtUtc));
        if (RenewalCancellationRequestedAtUtc is not null)
            return false;

        RenewalCancellationRequestedAtUtc = requestedAtUtc;
        CancelAtPeriodEndOn(requestedAtUtc);
        return true;
    }

    public bool CompleteRenewalCancellation(DateTimeOffset completedAtUtc)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        if (RenewalCancellationRequestedAtUtc is null || RenewalCancellationCompletedAtUtc is not null)
            return false;
        if (completedAtUtc < RenewalCancellationRequestedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc), "Provider cancellation cannot complete before it is requested.");

        RenewalCancellationCompletedAtUtc = completedAtUtc;
        return true;
    }

    /// <summary>
    /// Records a verified failed charge for a billing cycle. Only the first failed outcome for
    /// that cycle asks the caller to enqueue the failed-renewal notice.
    /// </summary>
    public OrganizationSubscriptionTransition ApplyRenewalFailure(
        string providerCycleId,
        DateTimeOffset periodStartsAtUtc,
        DateTimeOffset periodEndsAtUtc,
        DateTimeOffset appliedAtUtc)
    {
        EnsureUtc(appliedAtUtc, nameof(appliedAtUtc));
        ValidateCyclePeriod(periodStartsAtUtc, periodEndsAtUtc);
        var cycle = FindCycle(providerCycleId, periodStartsAtUtc, periodEndsAtUtc);
        if (CancelAtPeriodEnd && cycle is null)
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Canceled);
        if (cycle is null && (periodStartsAtUtc != PaidThroughAtUtc || periodEndsAtUtc <= PaidThroughAtUtc))
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Stale);
        cycle ??= CreateCycle(providerCycleId, periodStartsAtUtc, periodEndsAtUtc);
        if (cycle.Status == OrganizationSubscriptionBillingCycleStatus.Pending && cycle.PeriodEndsAtUtc <= PaidThroughAtUtc)
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Stale);

        if (cycle.RecordFailure(appliedAtUtc))
            return OrganizationSubscriptionTransition.Changed(OrganizationSubscriptionTransitionKind.RenewalFailed, sendFailureNotice: true);

        return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Duplicate);
    }

    /// <summary>
    /// Records a verified successful renewal. A duplicate/stale event cannot extend access twice,
    /// and cancellation blocks any later renewal from advancing the paid-through boundary.
    /// </summary>
    public OrganizationSubscriptionTransition ApplyRenewalSuccess(
        string providerCycleId,
        DateTimeOffset periodStartsAtUtc,
        DateTimeOffset periodEndsAtUtc,
        DateTimeOffset appliedAtUtc)
    {
        EnsureUtc(appliedAtUtc, nameof(appliedAtUtc));
        ValidateCyclePeriod(periodStartsAtUtc, periodEndsAtUtc);
        var cycle = FindCycle(providerCycleId, periodStartsAtUtc, periodEndsAtUtc);
        var isNew = cycle is null;
        if (RenewalCancellationRequestedAtUtc is not null &&
            (cycle is null || cycle.Status != OrganizationSubscriptionBillingCycleStatus.Succeeded))
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Canceled);
        if (isNew && (periodStartsAtUtc != PaidThroughAtUtc || periodEndsAtUtc <= PaidThroughAtUtc))
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Stale);
        if (isNew && CancelAtPeriodEnd &&
            (CancellationSettlementApplied || periodStartsAtUtc != CancellationBoundaryAtUtc))
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Canceled);
        cycle ??= CreateCycle(providerCycleId, periodStartsAtUtc, periodEndsAtUtc);
        if (cycle.Status == OrganizationSubscriptionBillingCycleStatus.Succeeded)
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Duplicate);
        if (cycle.PeriodEndsAtUtc <= PaidThroughAtUtc)
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Stale);

        if (!cycle.RecordSuccess(appliedAtUtc))
            return OrganizationSubscriptionTransition.NoChange(OrganizationSubscriptionTransitionKind.Duplicate);

        PaidThroughAtUtc = cycle.PeriodEndsAtUtc;
        if (CancelAtPeriodEnd)
        {
            CancellationSettlementApplied = true;
            AccessExpiryReminderQueuedAtUtc = null;
            AccessExpiryReminderRetryAfterUtc = null;
        }
        return OrganizationSubscriptionTransition.Changed(OrganizationSubscriptionTransitionKind.RenewalSucceeded, sendSuccessNotice: true);
    }

    public bool MarkAccessExpiryReminderQueued(DateTimeOffset queuedAtUtc)
    {
        EnsureUtc(queuedAtUtc, nameof(queuedAtUtc));
        if (!CancelAtPeriodEnd || queuedAtUtc >= PaidThroughAtUtc || AccessExpiryReminderQueuedAtUtc is not null)
            return false;

        AccessExpiryReminderQueuedAtUtc = queuedAtUtc;
        AccessExpiryReminderRetryAfterUtc = null;
        return true;
    }

    public bool ScheduleAccessExpiryReminderRetry(DateTimeOffset retryAfterUtc)
    {
        EnsureUtc(retryAfterUtc, nameof(retryAfterUtc));
        if (!CancelAtPeriodEnd || AccessExpiryReminderQueuedAtUtc is not null || retryAfterUtc >= PaidThroughAtUtc)
            return false;

        AccessExpiryReminderRetryAfterUtc = retryAfterUtc;
        return true;
    }

    private OrganizationSubscriptionBillingCycle? FindCycle(
        string providerCycleId,
        DateTimeOffset periodStartsAtUtc,
        DateTimeOffset periodEndsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(providerCycleId) || providerCycleId.Length > 128)
            throw new ArgumentException("A provider billing-cycle identity is required.", nameof(providerCycleId));
        var existing = _billingCycles.SingleOrDefault(cycle => cycle.ProviderCycleId == providerCycleId);
        if (existing is not null)
        {
            if (existing.PeriodStartsAtUtc != periodStartsAtUtc || existing.PeriodEndsAtUtc != periodEndsAtUtc)
                throw new InvalidOperationException("A provider cycle identity cannot be reused for another billing period.");
            return existing;
        }

        return null;
    }

    private OrganizationSubscriptionBillingCycle CreateCycle(
        string providerCycleId,
        DateTimeOffset periodStartsAtUtc,
        DateTimeOffset periodEndsAtUtc)
    {
        var cycle = OrganizationSubscriptionBillingCycle.Create(
            Guid.NewGuid(), Id, providerCycleId, periodStartsAtUtc, periodEndsAtUtc);
        _billingCycles.Add(cycle);
        return cycle;
    }

    internal static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }

    private static void ValidateCyclePeriod(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("A billing cycle must end after it starts.", nameof(endsAtUtc));
    }

    private static void ValidatePrice(decimal price)
    {
        if (price <= 0m || price > Plan.MaxPriceAmount || decimal.Round(price, 4) != price)
            throw new ArgumentOutOfRangeException(nameof(price), price, "Subscription price must fit numeric(19,4) and be positive.");
    }
}

public static class OrganizationSubscriptionTransitionKind
{
    public const string Cancelled = "Cancelled";
    public const string RenewalFailed = "RenewalFailed";
    public const string RenewalSucceeded = "RenewalSucceeded";
    public const string Duplicate = "Duplicate";
    public const string Stale = "Stale";
    public const string Canceled = "Canceled";
}

public sealed record OrganizationSubscriptionTransition(
    string Kind,
    bool ChangedState,
    bool SendFailureNotice,
    bool SendSuccessNotice)
{
    internal static OrganizationSubscriptionTransition Changed(
        string kind,
        bool sendFailureNotice = false,
        bool sendSuccessNotice = false) => new(kind, true, sendFailureNotice, sendSuccessNotice);

    internal static OrganizationSubscriptionTransition NoChange(string kind) => new(kind, false, false, false);
}
