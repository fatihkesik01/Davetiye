using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>
/// Makes email dispatch and account deletion linearizable. The marker transaction commits and
/// releases the shared account lock before the provider transport is called.
/// </summary>
public sealed class AccountOwnedEmailDispatchGate(
    IOutermostAccountQuotaTransactionRunner accountLock,
    IAccountDeletionStatusReader deletionStatus,
    IOutboxWorkStore outbox) : IEmailOwnedDispatchGate
{
    public Task<OwnedOutboxDispatchOutcome> AuthorizeAsync(
        OutboxClaimReceipt receipt,
        Guid ownerAccountId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (ownerAccountId == Guid.Empty || at.Offset != TimeSpan.Zero)
            throw new ArgumentException("A valid owner and UTC timestamp are required.");

        return accountLock.ExecuteAndCommitAsync(ownerAccountId, async token =>
        {
            // This read is inside the same account advisory-lock transaction as account deletion.
            // If deletion acquires the lock first, this message is completed without being sent.
            var suppress = await deletionStatus.IsDeletingAsync(ownerAccountId, token);
            return await outbox.AuthorizeOwnedDispatchAsync(receipt, ownerAccountId, suppress, at, token);
        }, cancellationToken);
    }
}
