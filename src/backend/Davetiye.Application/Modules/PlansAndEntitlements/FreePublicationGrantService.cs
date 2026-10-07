using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

namespace Davetiye.Application.Modules.PlansAndEntitlements;

public sealed class FreePublicationGrantService(
    IAccountReferenceValidator accountReferenceValidator,
    IInvitationOwnershipValidator invitationOwnershipValidator,
    IAccountQuotaTransactionRunner transactionRunner,
    IFreeGrantReservationStore reservationStore) : IFreePublicationGrantService
{
    public async Task<FreeGrantReservationResult> ReserveAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken)
    {
        ValidateIds(accountId, invitationId);

        return await transactionRunner.ExecuteAsync(
            accountId,
            async transactionCancellationToken =>
            {
                var denial = await ValidatePublicationTargetAsync(
                    accountId,
                    invitationId,
                    transactionCancellationToken);
                return denial ?? await reservationStore.TryReserveAsync(
                    accountId,
                    invitationId,
                    reservedAt,
                    transactionCancellationToken);
            },
            cancellationToken);
    }

    public async Task<FreeGrantReservationResult> ConsumeImmediatelyAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        ValidateIds(accountId, invitationId);

        return await transactionRunner.ExecuteAsync(
            accountId,
            async transactionCancellationToken =>
            {
                var denial = await ValidatePublicationTargetAsync(
                    accountId,
                    invitationId,
                    transactionCancellationToken);
                return denial ?? await reservationStore.TryConsumeAsync(
                    accountId,
                    invitationId,
                    consumedAt,
                    transactionCancellationToken);
            },
            cancellationToken);
    }

    public async Task<FreeGrantReservationResult> ConsumeReservedAtStartAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        ValidateIds(accountId, invitationId);

        return await transactionRunner.ExecuteAsync(
            accountId,
            async transactionCancellationToken =>
            {
                var denial = await ValidatePublicationTargetAsync(
                    accountId,
                    invitationId,
                    transactionCancellationToken);
                return denial ?? await reservationStore.TryConsumeExistingReservationAsync(
                    accountId,
                    invitationId,
                    consumedAt,
                    transactionCancellationToken);
            },
            cancellationToken);
    }

    private static void ValidateIds(Guid accountId, Guid invitationId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty.", nameof(invitationId));
        }
    }

    private async Task<FreeGrantReservationResult?> ValidatePublicationTargetAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var accountStatus = await accountReferenceValidator.GetStatusAsync(accountId, cancellationToken);
        if (accountStatus != AccountReferenceStatus.Verified)
        {
            return AccountDenied(accountStatus);
        }

        return await invitationOwnershipValidator.IsOwnedByAccountAsync(
            accountId,
            invitationId,
            cancellationToken)
            ? null
            : InvitationDenied();
    }

    private static FreeGrantReservationResult AccountDenied(AccountReferenceStatus status) =>
        new(
            status switch
            {
                AccountReferenceStatus.NotFound => FreeGrantReservationOutcome.AccountNotFound,
                AccountReferenceStatus.Unverified => FreeGrantReservationOutcome.AccountNotVerified,
                AccountReferenceStatus.Banned => FreeGrantReservationOutcome.AccountInactive,
                _ => FreeGrantReservationOutcome.AccountInactive
            },
            null);

    private static FreeGrantReservationResult InvitationDenied() =>
        new(FreeGrantReservationOutcome.InvitationNotFoundOrNotOwned, null);
}
