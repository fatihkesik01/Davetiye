namespace Davetiye.Domain.Modules.Payments;

/// <summary>Provider billing-cycle identity and the idempotent outcomes observed for it.</summary>
public sealed class OrganizationSubscriptionBillingCycle
{
    private OrganizationSubscriptionBillingCycle() { }

    public Guid Id { get; private set; }
    public Guid OrganizationSubscriptionId { get; private set; }
    public string ProviderCycleId { get; private set; } = string.Empty;
    public DateTimeOffset PeriodStartsAtUtc { get; private set; }
    public DateTimeOffset PeriodEndsAtUtc { get; private set; }
    public string Status { get; private set; } = OrganizationSubscriptionBillingCycleStatus.Pending;
    public DateTimeOffset? FirstFailureAtUtc { get; private set; }
    public DateTimeOffset? FailureNoticeRecordedAtUtc { get; private set; }
    public DateTimeOffset? SucceededAtUtc { get; private set; }
    public DateTimeOffset? SuccessNoticeRecordedAtUtc { get; private set; }

    internal static OrganizationSubscriptionBillingCycle Create(
        Guid id,
        Guid subscriptionId,
        string providerCycleId,
        DateTimeOffset periodStartsAtUtc,
        DateTimeOffset periodEndsAtUtc)
    {
        if (id == Guid.Empty || subscriptionId == Guid.Empty)
            throw new ArgumentException("Subscription and cycle identifiers must not be empty.");
        if (string.IsNullOrWhiteSpace(providerCycleId) || providerCycleId.Length > 128)
            throw new ArgumentException("A provider billing-cycle identity is required.", nameof(providerCycleId));
        OrganizationSubscription.EnsureUtc(periodStartsAtUtc, nameof(periodStartsAtUtc));
        OrganizationSubscription.EnsureUtc(periodEndsAtUtc, nameof(periodEndsAtUtc));
        if (periodEndsAtUtc <= periodStartsAtUtc)
            throw new ArgumentException("A billing cycle must end after it starts.", nameof(periodEndsAtUtc));

        return new OrganizationSubscriptionBillingCycle
        {
            Id = id,
            OrganizationSubscriptionId = subscriptionId,
            ProviderCycleId = providerCycleId,
            PeriodStartsAtUtc = periodStartsAtUtc,
            PeriodEndsAtUtc = periodEndsAtUtc
        };
    }

    internal bool RecordFailure(DateTimeOffset appliedAtUtc)
    {
        if (Status == OrganizationSubscriptionBillingCycleStatus.Succeeded)
            return false;

        if (Status == OrganizationSubscriptionBillingCycleStatus.Pending)
        {
            Status = OrganizationSubscriptionBillingCycleStatus.Failed;
            FirstFailureAtUtc = appliedAtUtc;
            FailureNoticeRecordedAtUtc = appliedAtUtc;
            return true;
        }

        return false;
    }

    internal bool RecordSuccess(DateTimeOffset appliedAtUtc)
    {
        if (Status == OrganizationSubscriptionBillingCycleStatus.Succeeded)
            return false;

        Status = OrganizationSubscriptionBillingCycleStatus.Succeeded;
        SucceededAtUtc = appliedAtUtc;
        SuccessNoticeRecordedAtUtc = appliedAtUtc;
        return true;
    }

    internal void RecordInitialActivation(DateTimeOffset appliedAtUtc)
    {
        Status = OrganizationSubscriptionBillingCycleStatus.Succeeded;
        SucceededAtUtc = appliedAtUtc;
    }
}

public static class OrganizationSubscriptionBillingCycleStatus
{
    public const string Pending = "Pending";
    public const string Failed = "Failed";
    public const string Succeeded = "Succeeded";
}
