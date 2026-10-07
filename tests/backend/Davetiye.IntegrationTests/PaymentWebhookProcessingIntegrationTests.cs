using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Notifications;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PaymentWebhookProcessingIntegrationTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Reference = "dv0123456789abcdef0123456789abcdef";
    private const string PaymentId = "123456789";
    private const string CheckoutToken = "cf311111-2222-4333-8444-555555555555";
    private const string ProviderName = PaymentWebhookProcessor.ProviderName;

    [Fact]
    public async Task Verified_success_commits_attempt_grant_purchase_outbox_and_inbox_once_and_replay_is_idempotent()
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));
        var processor = CreateProcessor(store, Verified());

        var outcome = await processor.ProcessAsync(database.Work, CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.Processed, outcome);
        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var grant = await database.Context.AccountPlanGrants.SingleAsync();
        var email = await database.Context.OutboxMessages.SingleAsync();
        var inbox = await database.Context.InboxMessages.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(PaymentId, attempt.ProviderPaymentId);
        Assert.Equal(grant.Id, attempt.GrantedPlanGrantId);
        Assert.Equal(attempt.AccountId, grant.AccountId);
        Assert.Equal(attempt.PlanId, grant.PlanId);
        Assert.Equal(attempt.InvitationId, grant.AssignedInvitationId);
        Assert.Equal(attempt.Id, email.Id);
        Assert.Equal(EmailOutboxWorker.MessageType, email.MessageType);
        Assert.NotNull(inbox.ProcessedAt);

        var replay = await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None);
        Assert.Equal(PaymentWebhookProcessOutcome.AlreadyHandled, replay);
        database.Context.ChangeTracker.Clear();
        Assert.Single(await database.Context.AccountPlanGrants.ToListAsync());
        Assert.Single(await database.Context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Deletion_wins_account_lock_then_verified_payment_keeps_settlement_evidence_without_grant_or_refund()
    {
        var database = await SeedAsync();
        var accountId = await database.Context.PaymentAttempts.Select(item => item.AccountId).SingleAsync();
        var deletionMail = new CapturingDeletionEmailSender();
        var deletion = CreateDeletionService(database.Context, deletionMail);
        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await deletion.RequestAsync(accountId, CancellationToken.None));
        var link = Assert.Single(deletionMail.Links);
        var token = QueryHelpers.ParseQuery(new Uri(link).Fragment.TrimStart('#'))["token"].ToString();
        Assert.Equal(AccountDeletionConfirmationOutcome.Confirmed,
            await deletion.ConfirmAsync(token, CancellationToken.None));

        var store = new PaymentWebhookProcessingStore(database.Context,
            new RecordingEmailSender(database.Context), new FrozenClock(Now));
        Assert.Equal(PaymentWebhookProcessOutcome.Processed,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(PaymentAttemptSettlementDisposition.NoEntitlement, attempt.SettlementDisposition);
        Assert.Equal(PaymentId, attempt.ProviderPaymentId);
        Assert.Null(attempt.GrantedPlanGrantId);
        Assert.Null(attempt.ReversedAtUtc);
        Assert.Empty(await database.Context.AccountPlanGrants.ToListAsync());
    }

    [Fact]
    public async Task Payment_wins_account_lock_then_deletion_revokes_the_existing_grant_without_refund()
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context,
            new RecordingEmailSender(database.Context), new FrozenClock(Now));
        Assert.Equal(PaymentWebhookProcessOutcome.Processed,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));
        var grant = await database.Context.AccountPlanGrants.SingleAsync();
        var accountId = grant.AccountId;
        var deletionMail = new CapturingDeletionEmailSender();
        var deletion = CreateDeletionService(database.Context, deletionMail);
        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await deletion.RequestAsync(accountId, CancellationToken.None));
        var token = QueryHelpers.ParseQuery(new Uri(Assert.Single(deletionMail.Links)).Fragment.TrimStart('#'))["token"].ToString();
        Assert.Equal(AccountDeletionConfirmationOutcome.Confirmed,
            await deletion.ConfirmAsync(token, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var revoked = await database.Context.AccountPlanGrants.SingleAsync();
        Assert.Equal(PaymentAttemptSettlementDisposition.Granted, attempt.SettlementDisposition);
        Assert.NotNull(attempt.GrantedPlanGrantId);
        Assert.Equal(PaymentId, attempt.ProviderPaymentId);
        Assert.Null(attempt.ReversedAtUtc);
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task Confirmed_terminal_failure_marks_attempt_and_inbox_without_creating_grant()
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));

        var outcome = await CreateProcessor(store, Verified("FAILURE", -1)).ProcessAsync(database.Work, CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.Processed, outcome);
        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Null(attempt.GrantedPlanGrantId);
        Assert.Empty(await database.Context.AccountPlanGrants.ToListAsync());
        Assert.Single(await database.Context.OutboxMessages.ToListAsync());
        Assert.NotNull((await database.Context.InboxMessages.SingleAsync()).ProcessedAt);
    }

    [Fact]
    public async Task Verified_checkout_settles_using_attempt_kind_after_plan_kind_changes()
    {
        var database = await SeedAsync();
        var plan = await database.Context.Plans.SingleAsync(candidate => candidate.Key == "standard");
        plan.UpdateAdminMetadata(plan.DisplayName, plan.Description, plan.PriceAmount, PlanBillingKind.Monthly);
        await database.Context.SaveChangesAsync();

        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));
        Assert.Equal(PaymentWebhookProcessOutcome.Processed,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var grant = await database.Context.AccountPlanGrants.SingleAsync();
        Assert.Equal(PaymentBillingKind.OneTime, attempt.BillingKindAtAttempt);
        var connection = database.Context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT billing_kind_at_attempt FROM payment_attempts WHERE id = @id";
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "id";
        idParameter.Value = attempt.Id;
        command.Parameters.Add(idParameter);
        Assert.Equal("OneTime", await command.ExecuteScalarAsync());
        Assert.Equal(PlanBillingKind.Monthly, (await database.Context.Plans.SingleAsync(item => item.Id == plan.Id)).BillingKind);
        Assert.Equal(PlanBillingKind.OneTime, grant.BillingKindAtGrant);
    }

    [Fact]
    public async Task Provider_outage_records_retry_and_leaves_attempt_and_entitlements_untouched()
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));

        var outcome = await CreateProcessor(store, new(ProviderPaymentVerificationOutcome.Unavailable))
            .ProcessAsync(database.Work, CancellationToken.None);

        Assert.Equal(PaymentWebhookProcessOutcome.RetryScheduled, outcome);
        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var inbox = await database.Context.InboxMessages.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(1, inbox.AttemptCount);
        Assert.Equal(Now.AddSeconds(15), inbox.NextAttemptAt);
        Assert.Empty(await database.Context.AccountPlanGrants.ToListAsync());
        Assert.Empty(await database.Context.OutboxMessages.ToListAsync());
        Assert.Null(inbox.ProcessedAt);
    }

    [Theory]
    [InlineData(VerifiedPaymentReversalOutcome.FinalLostChargeback, PaymentAttemptReversalKind.FinalLostChargeback,
        VerifiedPaymentReversalOutcome.FullRefund)]
    [InlineData(VerifiedPaymentReversalOutcome.FullRefund, PaymentAttemptReversalKind.FullRefund,
        VerifiedPaymentReversalOutcome.FinalLostChargeback)]
    public async Task Trusted_terminal_reversal_revokes_only_linked_grant_and_stops_current_publication_idempotently(
        VerifiedPaymentReversalOutcome appliedOutcome,
        string expectedReversalKind,
        VerifiedPaymentReversalOutcome conflictingOutcome)
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));
        // The trusted reversal source must retain/retry this result; it is not acknowledged as applied
        // until the authoritative successful payment has been settled.
        Assert.Equal(PaymentReversalStoreOutcome.NotFound,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.FinalLostChargeback,
                Now.AddMinutes(1), CancellationToken.None));
        Assert.Equal(PaymentWebhookProcessOutcome.Processed,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var invitation = await database.Context.Invitations.SingleAsync();
        var grant = await database.Context.AccountPlanGrants.SingleAsync();
        invitation.PinTemplate("classic-wedding", 1);
        invitation.BeginInitialPublication(scheduled: false);
        database.Context.PublicationWindows.Add(PublicationWindow.Create(Guid.NewGuid(), invitation.Id, grant.Id,
            Now, Now.AddDays(7), "UTC", Now));
        await database.Context.SaveChangesAsync();

        Assert.Equal(PaymentReversalStoreOutcome.NoAccessChange,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.OpenDispute,
                Now.AddMinutes(1), CancellationToken.None));
        Assert.Equal(PaymentReversalStoreOutcome.NotFound,
            await store.ApplyVerifiedReversalAsync("987654321", VerifiedPaymentReversalOutcome.FullRefund,
                Now.AddMinutes(1), CancellationToken.None));
        database.Context.ChangeTracker.Clear();
        Assert.Null((await database.Context.AccountPlanGrants.SingleAsync()).RevokedAt);
        Assert.Equal(InvitationStoredState.Active, (await database.Context.Invitations.SingleAsync()).State);
        Assert.True((await database.Context.PublicationWindows.SingleAsync()).IsCurrent);

        Assert.Equal(PaymentReversalStoreOutcome.Revoked,
            await store.ApplyVerifiedReversalAsync(PaymentId, appliedOutcome,
                Now.AddMinutes(1), CancellationToken.None));
        Assert.Equal(PaymentReversalStoreOutcome.AlreadyRevoked,
            await store.ApplyVerifiedReversalAsync(PaymentId, appliedOutcome,
                Now.AddMinutes(2), CancellationToken.None));
        Assert.Equal(PaymentReversalStoreOutcome.ConflictingOutcome,
            await store.ApplyVerifiedReversalAsync(PaymentId, conflictingOutcome,
                Now.AddMinutes(2), CancellationToken.None));
        Assert.Equal(conflictingOutcome == VerifiedPaymentReversalOutcome.FinalLostChargeback
                ? PaymentReversalStoreOutcome.ConflictingOutcome
                : PaymentReversalStoreOutcome.NoAccessChange,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.FinalWonChargeback,
                Now.AddMinutes(2), CancellationToken.None));

        // A delayed duplicate success HPP callback is acknowledged but cannot reopen a reversed attempt.
        Assert.Equal(PaymentWebhookProcessOutcome.AlreadyHandled,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        Assert.NotNull((await database.Context.AccountPlanGrants.SingleAsync()).RevokedAt);
        Assert.Equal(PaymentAttemptStatus.Reversed, (await database.Context.PaymentAttempts.SingleAsync()).Status);
        Assert.Equal(expectedReversalKind,
            (await database.Context.PaymentAttempts.SingleAsync()).ReversalKind);
        Assert.NotNull((await database.Context.PaymentAttempts.SingleAsync()).ReversedAtUtc);
        Assert.Equal(InvitationStoredState.Draft, (await database.Context.Invitations.SingleAsync()).State);
        Assert.False((await database.Context.PublicationWindows.SingleAsync()).IsCurrent);
        Assert.Single(await database.Context.AccountPlanGrants.ToListAsync());
        Assert.Single(await database.Context.Invitations.ToListAsync());
    }

    [Theory]
    [InlineData(VerifiedPaymentReversalOutcome.FullRefund, PaymentReversalStoreOutcome.Revoked)]
    [InlineData(VerifiedPaymentReversalOutcome.FinalLostChargeback, PaymentReversalStoreOutcome.ConflictingOutcome)]
    public async Task Final_won_chargeback_is_persisted_and_blocks_stale_loss_but_not_full_refund(
        VerifiedPaymentReversalOutcome laterOutcome,
        PaymentReversalStoreOutcome expectedLaterOutcome)
    {
        var database = await SeedAsync();
        var store = new PaymentWebhookProcessingStore(database.Context, new RecordingEmailSender(database.Context), new FrozenClock(Now));
        Assert.Equal(PaymentReversalStoreOutcome.NotFound,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.FinalWonChargeback,
                Now.AddMinutes(1), CancellationToken.None));
        Assert.Equal(PaymentWebhookProcessOutcome.Processed,
            await CreateProcessor(store, Verified()).ProcessAsync(database.Work, CancellationToken.None));

        Assert.Equal(PaymentReversalStoreOutcome.NoAccessChange,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.FinalWonChargeback,
                Now.AddMinutes(2), CancellationToken.None));
        Assert.Equal(PaymentReversalStoreOutcome.AlreadyResolved,
            await store.ApplyVerifiedReversalAsync(PaymentId, VerifiedPaymentReversalOutcome.FinalWonChargeback,
                Now.AddMinutes(3), CancellationToken.None));
        Assert.Equal(expectedLaterOutcome,
            await store.ApplyVerifiedReversalAsync(PaymentId, laterOutcome, Now.AddMinutes(4), CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var attempt = await database.Context.PaymentAttempts.SingleAsync();
        var grant = await database.Context.AccountPlanGrants.SingleAsync();
        Assert.Equal(PaymentAttemptChargebackResolution.FinalWon, attempt.ChargebackResolution);
        Assert.Equal(Now.AddMinutes(2), attempt.ChargebackResolvedAtUtc);
        if (laterOutcome == VerifiedPaymentReversalOutcome.FullRefund)
        {
            Assert.Equal(PaymentAttemptStatus.Reversed, attempt.Status);
            Assert.Equal(PaymentAttemptReversalKind.FullRefund, attempt.ReversalKind);
            Assert.NotNull(grant.RevokedAt);
        }
        else
        {
            Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
            Assert.Null(attempt.ReversalKind);
            Assert.Null(grant.RevokedAt);
        }
    }

    private async Task<SeededDatabase> SeedAsync()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        await new PlanCatalogInitializer(context).InitializeAsync(CancellationToken.None);
        var plan = await context.Plans.SingleAsync(candidate => candidate.Key == "standard");
        var identityUserId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = identityUserId,
            UserName = $"payment-{identityUserId:N}",
            NormalizedUserName = $"PAYMENT-{identityUserId:N}",
            Email = $"payment-{identityUserId:N}@example.test",
            NormalizedEmail = $"PAYMENT-{identityUserId:N}@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        var account = Account.Create(accountId, identityUserId, AccountType.Individual, "Payment test", Now);
        var invitation = Invitation.Create(invitationId, accountId, new string('a', PublicInvitationCode.EncodedLength), Now);
        var attempt = PaymentAttempt.Create(attemptId, accountId, invitationId, plan.Id, plan.Key, plan.PriceAmount,
            plan.Currency, "integration-payment-once", Reference, Now);
        attempt.SetCheckout("https://checkout.example.test/session", CheckoutToken, Now);
        var eventId = EventId();
        var payload = $$"""{"schemaVersion":1,"provider":"iyzico","format":"hpp","eventType":"CHECKOUT_FORM_AUTH","paymentId":"{{PaymentId}}","paymentConversationId":"{{Reference}}","status":"SUCCESS"}""";
        var inbox = InboxMessage.Create(Guid.NewGuid(), ProviderName, eventId, payload, Now);
        context.Users.Add(user);
        context.Accounts.Add(account);
        context.Invitations.Add(invitation);
        context.PaymentAttempts.Add(attempt);
        context.InboxMessages.Add(inbox);
        await context.SaveChangesAsync();
        return new SeededDatabase(context, new PaymentWebhookWorkItem(inbox.Id, eventId, payload, 0));
    }

    private static PaymentWebhookProcessor CreateProcessor(IPaymentWebhookProcessingStore store, ProviderPaymentVerificationResult result) =>
        new(store, new FakeVerifier(result), new FrozenClock(Now), Options.Create(new PaymentWebhookProcessingOptions()));

    private static ProviderPaymentVerificationResult Verified(string status = "SUCCESS", int fraudStatus = 1) => new(
        ProviderPaymentVerificationOutcome.Verified, PaymentId, "TRY", Reference, Reference, 699m, 699m,
        "success", status, fraudStatus, CheckoutToken);

    private static string EventId() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        "CHECKOUT_FORM_AUTH" + PaymentId + CheckoutToken + Reference + "SUCCESS"))).ToLowerInvariant();

    private static DavetiyeDbContext CreateDbContext(string connectionString) => new(
        new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connectionString,
            options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

    private sealed record SeededDatabase(DavetiyeDbContext Context, PaymentWebhookWorkItem Work);

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeVerifier(ProviderPaymentVerificationResult result) : IPaymentResultVerifier
    {
        public Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(string paymentId, string checkoutToken,
            string conversationId, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class RecordingEmailSender(DavetiyeDbContext context) : IEmailSender
    {
        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Settlement must use a stable outbox identity.");

        public Task SendOnceAsync(Guid messageId, string toEmail, string kind,
            IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
        {
            context.OutboxMessages.Add(OutboxMessage.Create(messageId, EmailOutboxWorker.MessageType,
                $"test-envelope:{kind}:{toEmail}", Now));
            return Task.CompletedTask;
        }
    }

    private AccountDeletionService CreateDeletionService(DavetiyeDbContext db, CapturingDeletionEmailSender sender) =>
        new(db, new AccountQuotaTransactionRunner(db),
            new OrganizationSubscriptionAccountDeletionHandler(db, new OutboxWorkStore(db)),
            new InvitationAccountDeletionHandler(db), new AccountPlanGrantDeletionHandler(db), sender,
            new FrozenClock(Now), Options.Create(new EmailTokenOptions { TokenLifetimeMinutes = 60 }),
            Options.Create(new PublicWebOptions { BaseUrl = "https://example.test" }));

    private sealed class CapturingDeletionEmailSender : IEmailSender
    {
        public List<string> Links { get; } = [];
        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SendShortLivedForAccountAsync(Guid ownerAccountId, string toEmail, string kind,
            IReadOnlyDictionary<string, string> data, DateTimeOffset protectUntilUtc, CancellationToken cancellationToken)
        {
            Links.Add(data["confirmationLink"]);
            return Task.CompletedTask;
        }
    }
}
