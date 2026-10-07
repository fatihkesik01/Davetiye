using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Payments;

/// <summary>Uses local UTC time for subscription transitions; provider event timestamps are not authoritative.</summary>
public sealed class OrganizationSubscriptionLifecycleService(
    IOrganizationSubscriptionLifecycleStore store,
    IClock clock) : IOrganizationSubscriptionLifecycleService
{
    public Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        return store.GetAccessSnapshotAsync(accountId, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }

    public Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
        VerifiedOrganizationSubscriptionActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        return store.ActivateFromVerifiedInitialPaymentAsync(activation, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }

    public Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
        VerifiedOrganizationSubscriptionRenewal renewal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        return store.ApplyVerifiedRenewalAsync(renewal, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }

    public Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(
        Guid accountId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || subscriptionId == Guid.Empty)
            throw new ArgumentException("Account and subscription identifiers are required.");
        return store.CancelAtPeriodEndAsync(accountId, subscriptionId, clock.UtcNow.ToUniversalTime(), cancellationToken);
    }
}
