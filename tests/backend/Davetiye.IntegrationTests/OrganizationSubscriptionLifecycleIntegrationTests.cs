using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Notifications;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrganizationSubscriptionLifecycleIntegrationTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Verified_activation_renewal_failure_retry_success_and_cancellation_commit_once_with_notifications()
    {
        var database = await SeedAsync();
        var store = CreateStore(database.Context);
        var activation = Activation(database.AccountId, database.PlanId, "provider-sub-1", "cycle-initial",
            Now, Now.AddDays(30));

        var initial = await store.ActivateFromVerifiedInitialPaymentAsync(activation, Now, CancellationToken.None);
        var replay = await store.ActivateFromVerifiedInitialPaymentAsync(activation, Now, CancellationToken.None);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Applied, initial.Outcome);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Duplicate, replay.Outcome);
        Assert.False(initial.EnqueueSuccessNotice);

        var failed = Renewal("provider-sub-1", "cycle-2", Now.AddDays(30), Now.AddDays(61),
            OrganizationSubscriptionRenewalOutcome.Failed);
        var failure = await store.ApplyVerifiedRenewalAsync(failed, Now.AddDays(30), CancellationToken.None);
        var retry = await store.ApplyVerifiedRenewalAsync(failed, Now.AddDays(30).AddHours(3), CancellationToken.None);
        var successful = await store.ApplyVerifiedRenewalAsync(
            Renewal("provider-sub-1", "cycle-2", Now.AddDays(30), Now.AddDays(61),
                OrganizationSubscriptionRenewalOutcome.Succeeded), Now.AddDays(30).AddHours(5), CancellationToken.None);

        Assert.True(failure.EnqueueFailureNotice);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Duplicate, retry.Outcome);
        Assert.False(retry.EnqueueFailureNotice);
        Assert.True(successful.EnqueueSuccessNotice);
        Assert.Equal(Now.AddDays(61), successful.PaidThroughAtUtc);

        var subscriptionId = await database.Context.OrganizationSubscriptions.Select(row => row.Id).SingleAsync();
        var canceled = await store.CancelAtPeriodEndAsync(database.AccountId, subscriptionId, Now.AddDays(31), CancellationToken.None);
        var cancelReplay = await store.CancelAtPeriodEndAsync(database.AccountId, subscriptionId, Now.AddDays(31).AddMinutes(1), CancellationToken.None);
        Assert.True(canceled.EnqueueCancellationNotice);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Duplicate, cancelReplay.Outcome);
        var cancellationSnapshot = await store.GetAccessSnapshotAsync(database.AccountId,
            Now.AddDays(32), CancellationToken.None);
        Assert.NotNull(cancellationSnapshot);
        Assert.Equal(OrganizationSubscriptionAccessStatus.Canceled, cancellationSnapshot.Status);

        database.Context.ChangeTracker.Clear();
        Assert.Single(await database.Context.OrganizationSubscriptions.ToListAsync());
        Assert.Equal(2, await database.Context.OrganizationSubscriptionBillingCycles.CountAsync());
        Assert.Single(await database.Context.AccountPlanGrants.Where(grant => grant.Source == GrantSource.OrganizationSubscription).ToListAsync());
        Assert.Equal(3, await database.Context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Verified_monthly_activation_uses_checkout_snapshot_after_plan_kind_changes()
    {
        var database = await SeedAsync();
        var plan = await database.Context.Plans.SingleAsync(candidate => candidate.Id == database.PlanId);
        plan.UpdateAdminMetadata(plan.DisplayName, plan.Description, plan.PriceAmount, PlanBillingKind.OneTime);
        await database.Context.SaveChangesAsync();

        var activation = Activation(database.AccountId, database.PlanId, "provider-sub-kind-change", "cycle-kind-change",
            Now, Now.AddDays(30), plan.PriceAmount, PurchasableBillingKind.Monthly);
        var result = await CreateStore(database.Context).ActivateFromVerifiedInitialPaymentAsync(activation, Now, CancellationToken.None);

        Assert.Equal(OrganizationSubscriptionCommandOutcome.Applied, result.Outcome);
        Assert.Equal(PlanBillingKind.Monthly, (await database.Context.AccountPlanGrants.SingleAsync()).BillingKindAtGrant);
        Assert.Equal(PlanBillingKind.OneTime, (await database.Context.Plans.SingleAsync(item => item.Id == database.PlanId)).BillingKind);
    }

    [Fact]
    public async Task Existing_subscription_keeps_activation_price_after_plan_price_changes_and_new_activation_uses_new_price()
    {
        var database = await SeedAsync();
        var store = CreateStore(database.Context);
        const decimal originalPrice = 2499m;
        const decimal updatedPrice = 3199.9900m;
        var firstEnd = Now.AddDays(30);
        await store.ActivateFromVerifiedInitialPaymentAsync(
            Activation(database.AccountId, database.PlanId, "provider-sub-price-old", "cycle-price-old", Now, firstEnd, originalPrice),
            Now, CancellationToken.None);

        var plan = await database.Context.Plans.SingleAsync(candidate => candidate.Id == database.PlanId);
        plan.UpdateAdminMetadata(plan.DisplayName, plan.Description, updatedPrice, plan.BillingKind);
        await database.Context.SaveChangesAsync();
        var beforeRenewal = await store.GetAccessSnapshotAsync(database.AccountId, Now.AddDays(1), CancellationToken.None);
        Assert.NotNull(beforeRenewal);
        Assert.Equal(originalPrice, beforeRenewal.PriceAmount);

        var renewedThrough = firstEnd.AddDays(30);
        await store.ApplyVerifiedRenewalAsync(
            Renewal("provider-sub-price-old", "cycle-price-renewal", firstEnd, renewedThrough,
                OrganizationSubscriptionRenewalOutcome.Succeeded), firstEnd, CancellationToken.None);
        var afterRenewal = await store.GetAccessSnapshotAsync(database.AccountId, firstEnd.AddDays(1), CancellationToken.None);
        Assert.NotNull(afterRenewal);
        Assert.Equal(originalPrice, afterRenewal.PriceAmount);

        var newPeriodEnd = renewedThrough.AddDays(30);
        var newActivation = Activation(database.AccountId, database.PlanId, "provider-sub-price-new", "cycle-price-new",
            renewedThrough, newPeriodEnd, updatedPrice);
        var activated = await store.ActivateFromVerifiedInitialPaymentAsync(newActivation, renewedThrough, CancellationToken.None);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Applied, activated.Outcome);
        var current = await store.GetAccessSnapshotAsync(database.AccountId, renewedThrough.AddDays(1), CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(updatedPrice, current.PriceAmount);
        Assert.Contains(await database.Context.OrganizationSubscriptions.ToListAsync(), subscription =>
            subscription.ProviderSubscriptionId == "provider-sub-price-old" && subscription.PriceAmountAtActivation == originalPrice);
        Assert.Contains(await database.Context.OrganizationSubscriptions.ToListAsync(), subscription =>
            subscription.ProviderSubscriptionId == "provider-sub-price-new" && subscription.PriceAmountAtActivation == updatedPrice);
    }

    [Fact]
    public async Task Resubscription_after_paid_through_reuses_plan_grant_but_archives_old_current_window()
    {
        var database = await SeedAsync();
        var store = CreateStore(database.Context);
        var firstEnd = Now.AddMinutes(1);
        await store.ActivateFromVerifiedInitialPaymentAsync(
            Activation(database.AccountId, database.PlanId, "provider-sub-old", "old-cycle", Now, firstEnd), Now,
            CancellationToken.None);

        var oldGrant = await database.Context.AccountPlanGrants.SingleAsync(grant => grant.Source == GrantSource.OrganizationSubscription);
        var invitation = Invitation.Create(Guid.NewGuid(), database.AccountId, new string('b', PublicInvitationCode.EncodedLength), Now);
        invitation.PinTemplate("classic", 1);
        invitation.BeginInitialPublication(scheduled: false);
        var window = PublicationWindow.Create(Guid.NewGuid(), invitation.Id, oldGrant.Id,
            Now, Now.AddDays(10), "Europe/Istanbul", Now);
        database.Context.Invitations.Add(invitation);
        database.Context.PublicationWindows.Add(window);
        await database.Context.SaveChangesAsync();

        var reactivatedAt = firstEnd.AddTicks(1);
        var secondEnd = reactivatedAt.AddDays(30);
        var renewal = await store.ActivateFromVerifiedInitialPaymentAsync(
            Activation(database.AccountId, database.PlanId, "provider-sub-new", "new-cycle", reactivatedAt, secondEnd),
            reactivatedAt, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var persistedInvitation = await database.Context.Invitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        var persistedWindow = await database.Context.PublicationWindows.SingleAsync(candidate => candidate.Id == window.Id);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Applied, renewal.Outcome);
        Assert.Equal(InvitationStoredState.Expired, persistedInvitation.State);
        Assert.False(persistedWindow.IsCurrent);
        Assert.Equal(2, await database.Context.OrganizationSubscriptions.CountAsync());
        Assert.Single(await database.Context.AccountPlanGrants.Where(grant => grant.Source == GrantSource.OrganizationSubscription).ToListAsync());

        var gate = new PublicationGrantAccessValidator(database.Context,
            new OrganizationSubscriptionEntitlementReader(database.Context));
        var snapshot = await gate.ReadAsync(database.AccountId, oldGrant.Id, CancellationToken.None);
        Assert.NotNull(snapshot);
        // The entitlement is effective again, but archived window + Expired state require explicit republish.
        Assert.False(persistedWindow.IsCurrent);
        Assert.Equal(InvitationStoredState.Expired,
            InvitationEffectiveStateEvaluator.Evaluate(persistedInvitation.State, persistedWindow, reactivatedAt));
    }

    [Fact]
    public async Task Public_and_creator_access_snapshots_end_synchronously_at_paid_through()
    {
        var database = await SeedAsync();
        var store = CreateStore(database.Context);
        var paidThrough = Now.AddMinutes(1);
        await store.ActivateFromVerifiedInitialPaymentAsync(
            Activation(database.AccountId, database.PlanId, "provider-sub-boundary", "cycle-boundary", Now, paidThrough),
            Now, CancellationToken.None);

        var before = await store.GetAccessSnapshotAsync(database.AccountId, paidThrough.AddTicks(-1), CancellationToken.None);
        var at = await store.GetAccessSnapshotAsync(database.AccountId, paidThrough, CancellationToken.None);
        Assert.NotNull(before);
        Assert.NotNull(at);
        Assert.Equal(OrganizationSubscriptionAccessStatus.Active, before.Status);
        Assert.Equal(OrganizationSubscriptionAccessStatus.Expired, at.Status);
        Assert.False(at.AllowsPublicAccessAt(paidThrough));

        var subscriptionId = at.SubscriptionId;
        var lateCancel = await store.CancelAtPeriodEndAsync(database.AccountId, subscriptionId, paidThrough, CancellationToken.None);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Stale, lateCancel.Outcome);
        Assert.Empty(await database.Context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Canceled_subscription_expiry_reminder_is_queued_once_inside_seven_day_window()
    {
        var database = await SeedAsync();
        var emailSender = new RecordingEmailSender(database.Context);
        var store = new OrganizationSubscriptionLifecycleStore(
            database.Context, new AccountQuotaTransactionRunner(database.Context), emailSender);
        var paidThrough = Now.AddDays(5);
        await store.ActivateFromVerifiedInitialPaymentAsync(
            Activation(database.AccountId, database.PlanId, "provider-sub-reminder", "reminder-cycle", Now, paidThrough),
            Now, CancellationToken.None);
        var subscriptionId = await database.Context.OrganizationSubscriptions.Select(item => item.Id).SingleAsync();
        await store.CancelAtPeriodEndAsync(database.AccountId, subscriptionId, Now.AddHours(1), CancellationToken.None);

        var retryDelay = TimeSpan.FromMinutes(5);
        var first = await store.EnqueueDueAccessExpiryRemindersAsync(Now, 100, retryDelay, CancellationToken.None);
        var replay = await store.EnqueueDueAccessExpiryRemindersAsync(Now.AddHours(2), 100, retryDelay, CancellationToken.None);
        var expired = await store.EnqueueDueAccessExpiryRemindersAsync(paidThrough, 100, retryDelay, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, replay);
        Assert.Equal(0, expired);
        Assert.Single(emailSender.Messages, message =>
            message.Kind == EmailNotificationKinds.SubscriptionAccessExpiryReminder);
        Assert.Equal(2, await database.Context.OutboxMessages.CountAsync());
    }

    private async Task<SeededDatabase> SeedAsync()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        await new PlanCatalogInitializer(context).InitializeAsync(CancellationToken.None);
        var accountId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var planId = await context.Plans.Where(plan => plan.Key == "organization").Select(plan => plan.Id).SingleAsync();
        context.Users.Add(new ApplicationUser
        {
            Id = identityId,
            UserName = $"organization-{identityId:N}",
            NormalizedUserName = $"ORGANIZATION-{identityId:N}",
            Email = $"organization-{identityId:N}@example.test",
            NormalizedEmail = $"ORGANIZATION-{identityId:N}@EXAMPLE.TEST",
            EmailConfirmed = true
        });
        context.Accounts.Add(Account.Create(accountId, identityId, AccountType.Organization, "Organization test", Now));
        await context.SaveChangesAsync();
        return new(context, accountId, planId);
    }

    private static OrganizationSubscriptionLifecycleStore CreateStore(DavetiyeDbContext context) =>
        new(context, new AccountQuotaTransactionRunner(context), new RecordingEmailSender(context));

    private static VerifiedOrganizationSubscriptionActivation Activation(Guid accountId, Guid planId,
        string subscriptionId, string cycleId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        decimal priceAmountAtCheckout = 2499m,
        PurchasableBillingKind billingKindAtAttempt = PurchasableBillingKind.Monthly) =>
        new(accountId, planId, "test-provider", subscriptionId, cycleId, startsAt, endsAt, priceAmountAtCheckout, billingKindAtAttempt);

    private static VerifiedOrganizationSubscriptionRenewal Renewal(string subscriptionId, string cycleId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, OrganizationSubscriptionRenewalOutcome outcome) =>
        new("test-provider", subscriptionId, cycleId, startsAt, endsAt, outcome);

    private static DavetiyeDbContext CreateDbContext(string connectionString) => new(
        new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connectionString,
            options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

    private sealed record SeededDatabase(DavetiyeDbContext Context, Guid AccountId, Guid PlanId);

    private sealed class RecordingEmailSender(DavetiyeDbContext context) : IEmailSender
    {
        private readonly HashSet<Guid> messageIds = [];
        public List<(Guid MessageId, string ToEmail, string Kind, IReadOnlyDictionary<string, string> Data)> Messages { get; } = [];

        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken) => throw new NotSupportedException("Stable outbox ids are required.");

        public Task SendOnceAsync(Guid messageId, string toEmail, string kind,
            IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
        {
            if (!messageIds.Add(messageId))
                return Task.CompletedTask;
            Messages.Add((messageId, toEmail, kind, data));
            var payload = $"{toEmail}:{kind}:{string.Join(';', data.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"))}";
            context.OutboxMessages.Add(OutboxMessage.Create(messageId, EmailOutboxWorker.MessageType, payload, Now));
            return Task.CompletedTask;
        }
    }
}
