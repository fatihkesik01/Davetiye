using System.Security.Cryptography;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.Payments;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Persistence;

/// <summary>
/// Cross-module transaction adapter for the payment settlement use case. It keeps entity rows and
/// the transaction itself out of the Payments Application contract while atomically settling the
/// PaymentAttempt, issuing its grant, queueing one notification, and completing the inbox event.
/// </summary>
public sealed class PaymentWebhookProcessingStore(
    DavetiyeDbContext dbContext,
    IEmailSender emailSender,
    IClock clock) : IPaymentWebhookProcessingStore
{
    public async Task<IReadOnlyList<PaymentWebhookWorkItem>> ClaimAsync(
        string providerName,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero || nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var claimedUntil = nowUtc + leaseDuration;
        var claimed = await dbContext.InboxMessages
            .FromSqlInterpolated($"""
                WITH claimable AS (
                    SELECT id
                    FROM inbox_messages
                    WHERE provider_name = {providerName}
                      AND processed_at IS NULL
                      AND failed_permanently = false
                      AND next_attempt_at <= {nowUtc}
                      AND (claimed_until IS NULL OR claimed_until < {nowUtc})
                    ORDER BY next_attempt_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE inbox_messages AS m
                SET claimed_until = {claimedUntil}
                FROM claimable
                WHERE m.id = claimable.id
                RETURNING m.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return claimed.Select(message => new PaymentWebhookWorkItem(
            message.Id, message.ProviderEventId, message.Payload, message.AttemptCount)).ToArray();
    }

    public async Task<PaymentAttemptVerificationSnapshot?> FindAttemptAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.PaymentAttempts.AsNoTracking()
            .Where(candidate => candidate.Reference == reference)
            .Select(candidate => new PaymentAttemptVerificationSnapshot(
                candidate.Id, candidate.AccountId, candidate.InvitationId, candidate.PlanId,
                candidate.PlanKey, candidate.Amount, candidate.Currency, candidate.Reference,
                candidate.Status, candidate.ProviderCheckoutId, candidate.ProviderPaymentId,
                candidate.GrantedPlanGrantId))
            .SingleOrDefaultAsync(cancellationToken);
        return attempt;
    }

    public async Task<PaymentWebhookStoreOutcome> FinalizeAsync(
        PaymentWebhookFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        var serializedAccountId = await dbContext.PaymentAttempts.AsNoTracking()
            .Where(candidate => candidate.Reference == request.PaymentConversationId)
            .Select(candidate => (Guid?)candidate.AccountId).SingleOrDefaultAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (serializedAccountId is { } lockAccountId)
        {
            var lockKey = BitConverter.ToInt64(lockAccountId.ToByteArray(), 0);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        }
        var inbox = await LockInboxAsync(request.MessageId, cancellationToken);
        if (inbox is null || inbox.ProcessedAt is not null || inbox.FailedPermanently)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentWebhookStoreOutcome.AlreadyHandled;
        }

        var attempt = await LockAttemptByReferenceAsync(request.PaymentConversationId, cancellationToken);
        if (attempt is null || inbox.ProviderName != "iyzico-hpp" ||
            inbox.ProviderEventId != request.ProviderEventId ||
            request.PaymentConversationId != attempt.Reference)
        {
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);
        }

        if (!MatchesEventKey(inbox.ProviderEventId, request, attempt.ProviderCheckoutId))
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);

        if (attempt.Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Reversed or
            PaymentAttemptStatus.Failed or PaymentAttemptStatus.Canceled)
        {
            inbox.MarkProcessed(request.ProcessedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaymentWebhookStoreOutcome.AlreadyHandled;
        }

        var success = request.ProviderResponseStatus == "success" && request.PaymentStatus == "SUCCESS" && request.FraudStatus == 1;
        var terminalFailure = request.ProviderResponseStatus == "success" && request.PaymentStatus == "FAILURE" && request.FraudStatus == -1;
        if (!success && !terminalFailure)
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);

        if (request.PaymentId != request.VerifiedPaymentId ||
            request.Currency != attempt.Currency ||
            request.BasketId != attempt.Reference ||
            request.ConversationId != attempt.Reference ||
            request.Price != attempt.Amount || request.PaidPrice != attempt.Amount)
        {
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);
        }

        var plan = await dbContext.Plans.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attempt.PlanId, cancellationToken);
        var account = await dbContext.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attempt.AccountId, cancellationToken);
        if (account?.DeletionStartedAtUtc is not null && success)
        {
            if (!attempt.MarkSucceededWithoutEntitlement(request.ProcessedAtUtc, request.PaymentId))
            {
                inbox.MarkProcessed(request.ProcessedAtUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PaymentWebhookStoreOutcome.AlreadyHandled;
            }
            inbox.MarkProcessed(request.ProcessedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaymentWebhookStoreOutcome.Processed;
        }
        var invitationOwned = await dbContext.Invitations.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == attempt.InvitationId && candidate.AccountId == attempt.AccountId,
                cancellationToken);
        if (plan is null || account is null || account.AccountType != AccountType.Individual || !invitationOwned ||
            plan.Key != attempt.PlanKey || attempt.BillingKindAtAttempt != PaymentBillingKind.OneTime)
        {
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);
        }

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == account.IdentityUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user?.Email))
            return await FailLockedInboxAsync(inbox, transaction, cancellationToken);

        var now = request.ProcessedAtUtc;
        if (success)
        {
            var grantId = Guid.NewGuid();
            if (!attempt.MarkSucceeded(now, request.PaymentId, grantId))
            {
                inbox.MarkProcessed(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PaymentWebhookStoreOutcome.AlreadyHandled;
            }

            var grant = AccountPlanGrant.Create(grantId, attempt.AccountId, attempt.PlanId,
                GrantSource.IndividualPurchase, now);
            grant.ReserveForInvitation(attempt.InvitationId, now);
            dbContext.AccountPlanGrants.Add(grant);
            await emailSender.SendOnceForAccountAsync(attempt.AccountId, attempt.Id, user.Email, EmailNotificationKinds.PurchaseSucceeded,
                new Dictionary<string, string> { ["planName"] = plan.DisplayName }, cancellationToken);
        }
        else
        {
            if (!attempt.MarkFailed(now))
            {
                inbox.MarkProcessed(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PaymentWebhookStoreOutcome.AlreadyHandled;
            }

            await emailSender.SendOnceForAccountAsync(attempt.AccountId, attempt.Id, user.Email, EmailNotificationKinds.PurchaseFailed,
                new Dictionary<string, string>(), cancellationToken);
        }

        inbox.MarkProcessed(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentWebhookStoreOutcome.Processed;
    }

    public async Task<PaymentWebhookStoreOutcome> MarkHandledAsync(
        Guid messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var inbox = await LockInboxAsync(messageId, cancellationToken);
        if (inbox is null || inbox.ProcessedAt is not null || inbox.FailedPermanently)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentWebhookStoreOutcome.AlreadyHandled;
        }
        inbox.MarkProcessed(processedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentWebhookStoreOutcome.AlreadyHandled;
    }

    public async Task<PaymentWebhookStoreOutcome> RecordFailureAsync(
        Guid messageId,
        DateTimeOffset failedAtUtc,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var inbox = await LockInboxAsync(messageId, cancellationToken);
        if (inbox is null || inbox.ProcessedAt is not null || inbox.FailedPermanently)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentWebhookStoreOutcome.AlreadyHandled;
        }
        inbox.RecordFailedAttempt(failedAtUtc, nextAttemptAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return nextAttemptAtUtc is null
            ? PaymentWebhookStoreOutcome.PermanentlyFailed
            : PaymentWebhookStoreOutcome.RetryScheduled;
    }

    public async Task<PaymentReversalStoreOutcome> ApplyVerifiedReversalAsync(
        string providerPaymentId,
        VerifiedPaymentReversalOutcome outcome,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > 32 ||
            !providerPaymentId.All(char.IsAsciiDigit) || appliedAtUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(outcome))
            throw new ArgumentException("A verified payment identity, terminal outcome, and UTC timestamp are required.");

        var serializedAccountId = await dbContext.PaymentAttempts.AsNoTracking()
            .Where(candidate => candidate.ProviderPaymentId == providerPaymentId)
            .Select(candidate => (Guid?)candidate.AccountId).SingleOrDefaultAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (serializedAccountId is { } lockAccountId)
        {
            var lockKey = BitConverter.ToInt64(lockAccountId.ToByteArray(), 0);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        }
        var attempt = await dbContext.PaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM payment_attempts WHERE provider_payment_id = {providerPaymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (attempt is null || (attempt.Status != PaymentAttemptStatus.Succeeded && attempt.Status != PaymentAttemptStatus.Reversed) ||
            attempt.ProviderPaymentId != providerPaymentId || attempt.GrantedPlanGrantId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NotFound;
        }

        if (outcome == VerifiedPaymentReversalOutcome.OpenDispute)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NoAccessChange;
        }

        if (outcome == VerifiedPaymentReversalOutcome.FinalWonChargeback)
        {
            if (attempt.Status == PaymentAttemptStatus.Reversed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return attempt.ChargebackResolution == PaymentAttemptChargebackResolution.FinalLost
                    ? PaymentReversalStoreOutcome.NoAccessChange
                    : PaymentReversalStoreOutcome.ConflictingOutcome;
            }

            if (attempt.ChargebackResolution is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return attempt.ChargebackResolution == PaymentAttemptChargebackResolution.FinalWon
                    ? PaymentReversalStoreOutcome.AlreadyResolved
                    : PaymentReversalStoreOutcome.ConflictingOutcome;
            }

            if (!attempt.MarkChargebackResolved(appliedAtUtc, PaymentAttemptChargebackResolution.FinalWon))
            {
                await transaction.RollbackAsync(cancellationToken);
                return PaymentReversalStoreOutcome.NotFound;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NoAccessChange;
        }

        var grantId = attempt.GrantedPlanGrantId.Value;
        var grant = await dbContext.AccountPlanGrants
            .FromSqlInterpolated($"SELECT * FROM account_plan_grants WHERE id = {grantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (grant is null || grant.AccountId != attempt.AccountId || grant.PlanId != attempt.PlanId ||
            grant.Source != GrantSource.IndividualPurchase || grant.AssignedInvitationId != attempt.InvitationId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NotFound;
        }

        var reversalKind = outcome == VerifiedPaymentReversalOutcome.FullRefund
            ? PaymentAttemptReversalKind.FullRefund
            : PaymentAttemptReversalKind.FinalLostChargeback;
        if (attempt.Status == PaymentAttemptStatus.Reversed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return attempt.ReversalKind == reversalKind
                ? PaymentReversalStoreOutcome.AlreadyRevoked
                : PaymentReversalStoreOutcome.ConflictingOutcome;
        }

        if (grant.RevokedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NotFound;
        }

        if (appliedAtUtc < grant.GrantedAt)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NotFound;
        }

        if (outcome == VerifiedPaymentReversalOutcome.FinalLostChargeback)
        {
            if (attempt.ChargebackResolution == PaymentAttemptChargebackResolution.FinalWon)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PaymentReversalStoreOutcome.ConflictingOutcome;
            }

            if (attempt.ChargebackResolution is null &&
                !attempt.MarkChargebackResolved(appliedAtUtc, PaymentAttemptChargebackResolution.FinalLost))
            {
                await transaction.RollbackAsync(cancellationToken);
                return PaymentReversalStoreOutcome.NotFound;
            }
        }

        if (!attempt.MarkReversed(appliedAtUtc, reversalKind))
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentReversalStoreOutcome.NotFound;
        }
        grant.Revoke(appliedAtUtc);
        var invitation = await dbContext.Invitations
            .FromSqlInterpolated($"SELECT * FROM invitations WHERE id = {attempt.InvitationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is not null && invitation.AccountId == attempt.AccountId)
        {
            var window = await dbContext.PublicationWindows
                .FromSqlInterpolated($"SELECT * FROM publication_windows WHERE invitation_id = {invitation.Id} AND grant_id = {grantId} AND is_current = true FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (window is not null)
            {
                window.MarkHistorical();
                invitation.ChangePublicationState(Davetiye.Domain.Modules.Invitations.InvitationStoredState.Draft);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentReversalStoreOutcome.Revoked;
    }

    private async Task<PaymentWebhookStoreOutcome> FailLockedInboxAsync(
        InboxMessage inbox,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        inbox.RecordFailedAttempt(clock.UtcNow.ToUniversalTime(), nextAttemptAt: null);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentWebhookStoreOutcome.PermanentlyFailed;
    }

    private async Task<InboxMessage?> LockInboxAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.InboxMessages
            .FromSqlInterpolated($"SELECT * FROM inbox_messages WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<PaymentAttempt?> LockAttemptByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        await dbContext.PaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM payment_attempts WHERE reference = {reference} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static bool MatchesEventKey(string providerEventId, PaymentWebhookFinalizationRequest request, string? checkoutToken)
    {
        if (string.IsNullOrWhiteSpace(checkoutToken) || !Guid.TryParseExact(checkoutToken, "D", out _) ||
            providerEventId.Length != 64 || providerEventId.Any(character => !Uri.IsHexDigit(character)))
            return false;
        var expected = IyzicoHppWebhookPayload.ComputeProviderEventId(request.EventType, request.PaymentId,
            checkoutToken, request.PaymentConversationId, request.HppStatus);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(providerEventId));
    }
}
