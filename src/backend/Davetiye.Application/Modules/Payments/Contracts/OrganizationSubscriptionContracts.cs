namespace Davetiye.Application.Modules.Payments.Contracts;

public enum OrganizationSubscriptionRenewalOutcome
{
    Failed,
    Succeeded
}

/// <summary>Renewal details supplied only after provider verification.</summary>
public sealed record VerifiedOrganizationSubscriptionRenewal(
    string ProviderName,
    string ProviderSubscriptionId,
    string ProviderBillingCycleId,
    DateTimeOffset BillingPeriodStartsAtUtc,
    DateTimeOffset BillingPeriodEndsAtUtc,
    OrganizationSubscriptionRenewalOutcome Outcome);

/// <summary>Initial Organization charge after authoritative provider verification.</summary>
public sealed record VerifiedOrganizationSubscriptionActivation(
    Guid AccountId,
    Guid PlanId,
    string ProviderName,
    string ProviderSubscriptionId,
    string ProviderBillingCycleId,
    DateTimeOffset FirstPeriodStartsAtUtc,
    DateTimeOffset FirstPeriodEndsAtUtc,
    decimal PriceAmountAtCheckout,
    // Server-side checkout snapshot from the verified flow; this is not accepted from a client request.
    PurchasableBillingKind BillingKindAtAttempt);

public enum OrganizationSubscriptionCommandOutcome
{
    Applied,
    Duplicate,
    Stale,
    Canceled,
    NotFound,
    Conflict
}

/// <summary>Stable result for the transaction that applies a verified renewal or cancellation.</summary>
public sealed record OrganizationSubscriptionCommandResult(
    OrganizationSubscriptionCommandOutcome Outcome,
    bool EnqueueFailureNotice,
    bool EnqueueSuccessNotice,
    bool EnqueueCancellationNotice,
    DateTimeOffset? PaidThroughAtUtc);

/// <summary>Entitlement-facing data; provider identifiers are intentionally omitted.</summary>
public sealed record OrganizationSubscriptionAccessSnapshot(
    Guid SubscriptionId,
    Guid AccountId,
    Guid PlanId,
    DateTimeOffset PaidThroughAtUtc,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelRequestedAtUtc,
    string PlanDisplayName,
    decimal PriceAmount,
    string Currency,
    string BillingPeriod,
    string Status)
{
    public bool AllowsPublicAccessAt(DateTimeOffset instant) => instant < PaidThroughAtUtc;
}

public static class OrganizationSubscriptionAccessStatus
{
    public const string Active = "Active";
    public const string Canceled = "Canceled";
    public const string Expired = "Expired";
}

/// <summary>
/// Payments-owned atomic persistence boundary. Implementations must persist subscription/cycle
/// state and any requested notification outbox message in the same database transaction.
/// </summary>
public interface IOrganizationSubscriptionLifecycleStore
{
    Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(
        Guid accountId,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
        VerifiedOrganizationSubscriptionActivation activation,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
        VerifiedOrganizationSubscriptionRenewal renewal,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(
        Guid accountId,
        Guid subscriptionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken);
}

public interface IOrganizationSubscriptionLifecycleService
{
    Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
        VerifiedOrganizationSubscriptionActivation activation,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
        VerifiedOrganizationSubscriptionRenewal renewal,
        CancellationToken cancellationToken);

    Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(
        Guid accountId,
        Guid subscriptionId,
        CancellationToken cancellationToken);
}

public interface IOrganizationSubscriptionLifecycleJobs
{
    Task<int> EnqueueDueAccessExpiryRemindersAsync(
        DateTimeOffset evaluatedAtUtc,
        int batchSize,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}
