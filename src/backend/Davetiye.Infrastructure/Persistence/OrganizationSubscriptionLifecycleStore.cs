using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Davetiye.Infrastructure.Persistence;

/// <summary>
/// Applies verified Organization subscription transitions under the same per-account advisory
/// lock used by publication admission. Subscription/cycle/grant and notification outbox writes
/// share one PostgreSQL transaction.
/// </summary>
public sealed class OrganizationSubscriptionLifecycleStore(
    DavetiyeDbContext db,
    IOutermostAccountQuotaTransactionRunner accountLock,
    IEmailSender emailSender,
    ILogger<OrganizationSubscriptionLifecycleStore>? logger = null) : IOrganizationSubscriptionLifecycleStore, IOrganizationSubscriptionLifecycleJobs
{
    private static readonly TimeSpan AccessExpiryReminderLeadTime = TimeSpan.FromDays(7);

    public async Task<OrganizationSubscriptionAccessSnapshot?> GetAccessSnapshotAsync(
        Guid accountId,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureAccountAndUtc(accountId, evaluatedAtUtc);
        var subscriptions = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.AccountId == accountId && subscription.PaidThroughAtUtc > evaluatedAtUtc)
            .OrderByDescending(subscription => subscription.PaidThroughAtUtc)
            .Join(db.Plans.AsNoTracking(), subscription => subscription.PlanId, plan => plan.Id,
                (subscription, plan) => new OrganizationSubscriptionAccessSnapshot(
                    subscription.Id, subscription.AccountId, subscription.PlanId, subscription.PaidThroughAtUtc,
                    subscription.CancelAtPeriodEnd, subscription.CancelRequestedAtUtc,
                    plan.DisplayName, subscription.PriceAmountAtActivation, plan.Currency, "monthly",
                    subscription.CancelAtPeriodEnd
                        ? OrganizationSubscriptionAccessStatus.Canceled
                        : OrganizationSubscriptionAccessStatus.Active))
            .Take(2)
            .ToListAsync(cancellationToken);

        // More than one effective subscription is an integrity failure; access fails closed.
        if (subscriptions.Count > 1)
            return null;
        if (subscriptions.Count == 1)
            return subscriptions[0];

        // The same snapshot contract supports an expired status screen, while its caller can
        // still evaluate access synchronously at evaluatedAtUtc.
        return await db.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.AccountId == accountId)
            .OrderByDescending(subscription => subscription.PaidThroughAtUtc)
            .Join(db.Plans.AsNoTracking(), subscription => subscription.PlanId, plan => plan.Id,
                (subscription, plan) => new OrganizationSubscriptionAccessSnapshot(
                    subscription.Id, subscription.AccountId, subscription.PlanId, subscription.PaidThroughAtUtc,
                    subscription.CancelAtPeriodEnd, subscription.CancelRequestedAtUtc,
                    plan.DisplayName, subscription.PriceAmountAtActivation, plan.Currency, "monthly",
                    subscription.PaidThroughAtUtc <= evaluatedAtUtc
                        ? OrganizationSubscriptionAccessStatus.Expired
                        : subscription.CancelAtPeriodEnd
                            ? OrganizationSubscriptionAccessStatus.Canceled
                            : OrganizationSubscriptionAccessStatus.Active))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<OrganizationSubscriptionCommandResult> ActivateFromVerifiedInitialPaymentAsync(
        VerifiedOrganizationSubscriptionActivation activation,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        EnsureAccountAndUtc(activation.AccountId, appliedAtUtc);
        return accountLock.ExecuteAndCommitAsync(activation.AccountId,
            token => ActivateWithinLockAsync(activation, appliedAtUtc, token), cancellationToken);
    }

    public async Task<OrganizationSubscriptionCommandResult> ApplyVerifiedRenewalAsync(
        VerifiedOrganizationSubscriptionRenewal renewal,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renewal);
        EnsureUtc(appliedAtUtc, nameof(appliedAtUtc));

        var owner = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.ProviderName == renewal.ProviderName &&
                                   subscription.ProviderSubscriptionId == renewal.ProviderSubscriptionId)
            .Select(subscription => (Guid?)subscription.AccountId)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
            return Result(OrganizationSubscriptionCommandOutcome.NotFound);

        return await accountLock.ExecuteAndCommitAsync(owner.Value,
            token => ApplyRenewalWithinLockAsync(renewal, appliedAtUtc, token), cancellationToken);
    }

    public Task<OrganizationSubscriptionCommandResult> CancelAtPeriodEndAsync(
        Guid accountId,
        Guid subscriptionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureAccountAndUtc(accountId, requestedAtUtc);
        if (subscriptionId == Guid.Empty)
            throw new ArgumentException("Subscription id must not be empty.", nameof(subscriptionId));
        return accountLock.ExecuteAndCommitAsync(accountId,
            token => CancelWithinLockAsync(accountId, subscriptionId, requestedAtUtc, token), cancellationToken);
    }

    public async Task<int> EnqueueDueAccessExpiryRemindersAsync(
        DateTimeOffset evaluatedAtUtc,
        int batchSize,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        EnsureUtc(evaluatedAtUtc, nameof(evaluatedAtUtc));
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (retryDelay <= TimeSpan.Zero || retryDelay > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(retryDelay));

        var reminderThrough = evaluatedAtUtc + AccessExpiryReminderLeadTime;
        var candidates = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.CancelAtPeriodEnd &&
                                   subscription.AccessExpiryReminderQueuedAtUtc == null &&
                                   (subscription.AccessExpiryReminderRetryAfterUtc == null ||
                                    subscription.AccessExpiryReminderRetryAfterUtc <= evaluatedAtUtc) &&
                                   subscription.PaidThroughAtUtc > evaluatedAtUtc &&
                                   subscription.PaidThroughAtUtc <= reminderThrough)
            .OrderBy(subscription => subscription.AccessExpiryReminderRetryAfterUtc ?? DateTimeOffset.MinValue)
            .ThenBy(subscription => subscription.PaidThroughAtUtc)
            .Select(subscription => new { subscription.Id, subscription.AccountId })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                enqueued += await accountLock.ExecuteAndCommitAsync(candidate.AccountId, async token =>
                {
                    var subscription = await db.OrganizationSubscriptions.SingleOrDefaultAsync(item =>
                        item.Id == candidate.Id && item.AccountId == candidate.AccountId &&
                        item.CancelAtPeriodEnd && item.AccessExpiryReminderQueuedAtUtc == null &&
                        (item.AccessExpiryReminderRetryAfterUtc == null ||
                         item.AccessExpiryReminderRetryAfterUtc <= evaluatedAtUtc) &&
                        item.PaidThroughAtUtc > evaluatedAtUtc &&
                        item.PaidThroughAtUtc <= reminderThrough, token);
                    if (subscription is null)
                        return 0;
                    if (!subscription.MarkAccessExpiryReminderQueued(evaluatedAtUtc))
                        return 0;

                    var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(item =>
                        item.Id == candidate.AccountId && item.AccountType == AccountType.Organization, token);
                    var user = account is null ? null : await db.Users.AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == account.IdentityUserId, token);
                    var plan = await db.Plans.AsNoTracking()
                        .SingleOrDefaultAsync(item => item.Id == subscription.PlanId, token);
                    if (string.IsNullOrWhiteSpace(user?.Email) || plan is null)
                        throw new InvalidOperationException("A subscription owner email and plan are required for an expiry reminder.");

                    await emailSender.SendOnceForAccountAsync(candidate.AccountId,
                        StableMessageId("subscription-expiry-reminder", subscription.Id,
                            subscription.PaidThroughAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                        user.Email,
                        EmailNotificationKinds.SubscriptionAccessExpiryReminder,
                        new Dictionary<string, string>
                        {
                            ["planName"] = plan.DisplayName,
                            ["accessEndDate"] = subscription.PaidThroughAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        },
                        token);
                    await db.SaveChangesAsync(token);
                    return 1;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger?.LogWarning("Organization subscription reminder enqueue failed ({FailureType}).",
                    exception.GetType().Name);
                db.ChangeTracker.Clear();
                try
                {
                    var retryAtUtc = evaluatedAtUtc + retryDelay;
                    await accountLock.ExecuteAndCommitAsync(candidate.AccountId, async token =>
                    {
                        var subscription = await db.OrganizationSubscriptions.SingleOrDefaultAsync(item =>
                            item.Id == candidate.Id && item.AccountId == candidate.AccountId &&
                            item.CancelAtPeriodEnd && item.AccessExpiryReminderQueuedAtUtc == null &&
                            item.PaidThroughAtUtc > evaluatedAtUtc && item.PaidThroughAtUtc <= reminderThrough,
                            token);
                        if (subscription?.ScheduleAccessExpiryReminderRetry(retryAtUtc) == true)
                        {
                            await db.SaveChangesAsync(token);
                            return true;
                        }
                        return false;
                    }, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception retryException)
                {
                    logger?.LogWarning("Organization subscription reminder retry scheduling failed ({FailureType}).",
                        retryException.GetType().Name);
                }
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return enqueued;
    }

    private async Task<OrganizationSubscriptionCommandResult> ActivateWithinLockAsync(
        VerifiedOrganizationSubscriptionActivation activation,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == activation.AccountId, cancellationToken);
        var plan = await db.Plans.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == activation.PlanId, cancellationToken);
        if (account is null || account.DeletionStartedAtUtc is not null ||
            account.AccountType != AccountType.Organization || plan is null ||
            !plan.IsActive || activation.BillingKindAtAttempt != PurchasableBillingKind.Monthly || plan.Key != "organization" ||
            activation.PriceAmountAtCheckout <= 0m || activation.PriceAmountAtCheckout > Plan.MaxPriceAmount ||
            decimal.Round(activation.PriceAmountAtCheckout, 4) != activation.PriceAmountAtCheckout)
            return Result(OrganizationSubscriptionCommandOutcome.NotFound);

        var providerMatch = await db.OrganizationSubscriptions
            .Include(subscription => subscription.BillingCycles)
            .SingleOrDefaultAsync(subscription => subscription.ProviderName == activation.ProviderName &&
                subscription.ProviderSubscriptionId == activation.ProviderSubscriptionId, cancellationToken);
        if (providerMatch is not null)
        {
            var matchingCycle = providerMatch.BillingCycles.SingleOrDefault(cycle =>
                cycle.ProviderCycleId == activation.ProviderBillingCycleId);
            if (providerMatch.AccountId == activation.AccountId && providerMatch.PlanId == activation.PlanId &&
                matchingCycle is not null && matchingCycle.Status == OrganizationSubscriptionBillingCycleStatus.Succeeded &&
                matchingCycle.PeriodStartsAtUtc == activation.FirstPeriodStartsAtUtc &&
                matchingCycle.PeriodEndsAtUtc == activation.FirstPeriodEndsAtUtc)
                return Result(OrganizationSubscriptionCommandOutcome.Duplicate, providerMatch.PaidThroughAtUtc);
            return Result(OrganizationSubscriptionCommandOutcome.Conflict);
        }

        var activeSubscription = await db.OrganizationSubscriptions.AsNoTracking()
            .AnyAsync(subscription => subscription.AccountId == activation.AccountId &&
                                      subscription.PaidThroughAtUtc > appliedAtUtc, cancellationToken);
        if (activeSubscription)
            return Result(OrganizationSubscriptionCommandOutcome.Conflict);

        var grantRows = await db.AccountPlanGrants
            .Where(grant => grant.AccountId == activation.AccountId && grant.Source == GrantSource.OrganizationSubscription &&
                            grant.PlanId == activation.PlanId && grant.RevokedAt == null)
            .OrderBy(grant => grant.GrantedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (grantRows.Count > 1)
            return Result(OrganizationSubscriptionCommandOutcome.Conflict);

        var subscription = OrganizationSubscription.ActivateFromVerifiedInitialPayment(
            Guid.NewGuid(), activation.AccountId, activation.PlanId, activation.PriceAmountAtCheckout, activation.ProviderName,
            activation.ProviderSubscriptionId, activation.ProviderBillingCycleId,
            activation.FirstPeriodStartsAtUtc, activation.FirstPeriodEndsAtUtc, appliedAtUtc);
        db.OrganizationSubscriptions.Add(subscription);

        if (grantRows.Count == 0)
        {
            var grant = AccountPlanGrant.Create(Guid.NewGuid(), activation.AccountId, activation.PlanId,
                GrantSource.OrganizationSubscription, appliedAtUtc);
            db.AccountPlanGrants.Add(grant);
        }

        await RetirePreviousOrganizationWindowsAsync(activation.AccountId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result(OrganizationSubscriptionCommandOutcome.Applied, subscription.PaidThroughAtUtc);
    }

    private async Task<OrganizationSubscriptionCommandResult> ApplyRenewalWithinLockAsync(
        VerifiedOrganizationSubscriptionRenewal renewal,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var subscription = await db.OrganizationSubscriptions
            .Include(candidate => candidate.BillingCycles)
            .SingleOrDefaultAsync(candidate => candidate.ProviderName == renewal.ProviderName &&
                candidate.ProviderSubscriptionId == renewal.ProviderSubscriptionId, cancellationToken);
        if (subscription is null)
            return Result(OrganizationSubscriptionCommandOutcome.NotFound);

        var anotherEffectiveSubscription = await db.OrganizationSubscriptions.AsNoTracking()
            .AnyAsync(candidate => candidate.AccountId == subscription.AccountId && candidate.Id != subscription.Id &&
                                   candidate.PaidThroughAtUtc > appliedAtUtc, cancellationToken);
        if (anotherEffectiveSubscription)
            return Result(OrganizationSubscriptionCommandOutcome.Conflict, subscription.PaidThroughAtUtc);

        var existingCycleIds = subscription.BillingCycles.Select(cycle => cycle.Id).ToHashSet();

        var outcome = renewal.Outcome switch
        {
            OrganizationSubscriptionRenewalOutcome.Failed => subscription.ApplyRenewalFailure(
                renewal.ProviderBillingCycleId, renewal.BillingPeriodStartsAtUtc,
                renewal.BillingPeriodEndsAtUtc, appliedAtUtc),
            OrganizationSubscriptionRenewalOutcome.Succeeded => subscription.ApplyRenewalSuccess(
                renewal.ProviderBillingCycleId, renewal.BillingPeriodStartsAtUtc,
                renewal.BillingPeriodEndsAtUtc, appliedAtUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(renewal), "Unsupported verified renewal outcome.")
        };

        if (!outcome.ChangedState)
            return Result(MapOutcome(outcome.Kind), subscription.PaidThroughAtUtc);

        // A renewal can append a newly-created cycle to the backing field of an already-tracked
        // subscription. EF may classify this application-assigned-key dependent as Modified;
        // explicitly mark only cycles that were not present when the aggregate was loaded as Added.
        foreach (var cycle in subscription.BillingCycles)
        {
            if (!existingCycleIds.Contains(cycle.Id))
                db.Entry(cycle).State = EntityState.Added;
        }

        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == subscription.AccountId &&
                candidate.AccountType == AccountType.Organization, cancellationToken);
        var user = account is null ? null : await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == account.IdentityUserId, cancellationToken);
        var plan = await db.Plans.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == subscription.PlanId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user?.Email) || plan is null)
            throw new InvalidOperationException("A subscription owner email and plan are required for renewal notification.");

        if (outcome.SendFailureNotice)
            await emailSender.SendOnceForAccountAsync(subscription.AccountId, StableMessageId("renewal-failed", subscription.Id, renewal.ProviderBillingCycleId),
                user.Email, EmailNotificationKinds.SubscriptionRenewalFailed,
                new Dictionary<string, string> { ["planName"] = plan.DisplayName }, cancellationToken);
        if (outcome.SendSuccessNotice)
            await emailSender.SendOnceForAccountAsync(subscription.AccountId, StableMessageId("renewal-succeeded", subscription.Id, renewal.ProviderBillingCycleId),
                user.Email, EmailNotificationKinds.SubscriptionRenewalSucceeded,
                new Dictionary<string, string> { ["planName"] = plan.DisplayName }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result(OrganizationSubscriptionCommandOutcome.Applied, subscription.PaidThroughAtUtc,
            outcome.SendFailureNotice, outcome.SendSuccessNotice);
    }

    private async Task<OrganizationSubscriptionCommandResult> CancelWithinLockAsync(
        Guid accountId,
        Guid subscriptionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        var subscription = await db.OrganizationSubscriptions.SingleOrDefaultAsync(candidate =>
            candidate.Id == subscriptionId && candidate.AccountId == accountId, cancellationToken);
        if (subscription is null)
            return Result(OrganizationSubscriptionCommandOutcome.NotFound);
        if (subscription.CancelAtPeriodEnd)
            return Result(OrganizationSubscriptionCommandOutcome.Duplicate, subscription.PaidThroughAtUtc);
        if (requestedAtUtc >= subscription.PaidThroughAtUtc)
            return Result(OrganizationSubscriptionCommandOutcome.Stale, subscription.PaidThroughAtUtc);

        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId && candidate.AccountType == AccountType.Organization,
                cancellationToken);
        var user = account is null ? null : await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == account.IdentityUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user?.Email))
            return Result(OrganizationSubscriptionCommandOutcome.NotFound, subscription.PaidThroughAtUtc);

        if (!subscription.CancelAtPeriodEndOn(requestedAtUtc))
            return Result(OrganizationSubscriptionCommandOutcome.Conflict, subscription.PaidThroughAtUtc);

        await emailSender.SendOnceForAccountAsync(accountId, StableMessageId("subscription-cancelled", subscription.Id, "cancellation"),
            user.Email, EmailNotificationKinds.SubscriptionCancellation,
            new Dictionary<string, string>
            {
                ["accessEndDate"] = subscription.PaidThroughAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result(OrganizationSubscriptionCommandOutcome.Applied, subscription.PaidThroughAtUtc,
            enqueueCancellation: true);
    }

    private async Task RetirePreviousOrganizationWindowsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var grantIds = await db.AccountPlanGrants.AsNoTracking()
            .Where(grant => grant.AccountId == accountId && grant.Source == GrantSource.OrganizationSubscription)
            .Select(grant => grant.Id)
            .ToListAsync(cancellationToken);
        if (grantIds.Count == 0)
            return;

        var windows = await db.PublicationWindows
            .Where(window => window.IsCurrent && grantIds.Contains(window.GrantId))
            .ToListAsync(cancellationToken);
        if (windows.Count == 0)
            return;

        var invitationIds = windows.Select(window => window.InvitationId).ToArray();
        var invitations = await db.Invitations.IgnoreQueryFilters()
            .Where(invitation => invitation.AccountId == accountId && invitationIds.Contains(invitation.Id))
            .ToDictionaryAsync(invitation => invitation.Id, cancellationToken);

        foreach (var window in windows)
        {
            window.MarkHistorical();
            if (invitations.TryGetValue(window.InvitationId, out var invitation) &&
                invitation.DeletedAt is null && invitation.State is InvitationStoredState.Active or
                    InvitationStoredState.Scheduled or InvitationStoredState.Paused)
            {
                invitation.ChangePublicationState(InvitationStoredState.Expired);
            }
        }
    }

    private Guid StableMessageId(string purpose, Guid subscriptionId, string cycleId)
    {
        var input = Encoding.UTF8.GetBytes($"davetiye:{purpose}:{subscriptionId:N}:{cycleId}");
        var hash = SHA256.HashData(input);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, guidBytes.Length).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static OrganizationSubscriptionCommandOutcome MapOutcome(string kind) => kind switch
    {
        OrganizationSubscriptionTransitionKind.Duplicate => OrganizationSubscriptionCommandOutcome.Duplicate,
        OrganizationSubscriptionTransitionKind.Stale => OrganizationSubscriptionCommandOutcome.Stale,
        OrganizationSubscriptionTransitionKind.Canceled => OrganizationSubscriptionCommandOutcome.Canceled,
        _ => OrganizationSubscriptionCommandOutcome.Conflict
    };

    private static OrganizationSubscriptionCommandResult Result(
        OrganizationSubscriptionCommandOutcome outcome,
        DateTimeOffset? paidThrough = null,
        bool enqueueFailure = false,
        bool enqueueSuccess = false,
        bool enqueueCancellation = false) => new(outcome, enqueueFailure, enqueueSuccess, enqueueCancellation, paidThrough);

    private static void EnsureAccountAndUtc(Guid accountId, DateTimeOffset instantUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        EnsureUtc(instantUtc, nameof(instantUtc));
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}
