namespace Davetiye.Application.Modules.Payments.Contracts;

/// <summary>
/// Payments-owned account-deletion command. It updates the local subscription and appends the
/// provider cancellation intent to the shared outbox without committing the caller's transaction.
/// </summary>
public interface IOrganizationSubscriptionAccountDeletionCommand
{
    Task QueueRenewalCancellationAsync(Guid accountId, DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken);
}
