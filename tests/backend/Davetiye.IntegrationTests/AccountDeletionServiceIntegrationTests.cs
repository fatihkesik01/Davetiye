using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Modules.Memories;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Modules.GiftRegistry;
using Davetiye.Infrastructure.Modules.Rsvp;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountDeletionServiceIntegrationTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Confirmation_anonymizes_identity_revokes_grants_and_schedules_every_invitation_for_immediate_m2_purge()
    {
        var (connectionString, db, account, identityUserId, invitationIds) = await SeedAsync();
        var mail = new RecordingEmailSender();
        var service = CreateService(db, mail, new FrozenClock(Now));

        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await service.RequestAsync(account.Id, CancellationToken.None));
        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await service.RequestAsync(account.Id, CancellationToken.None));
        var sent = Assert.Single(mail.Messages);
        Assert.Equal(account.Id, sent.OwnerAccountId);
        Assert.Equal(EmailNotificationKinds.AccountDeletionConfirmation, sent.Kind);
        var token = QueryHelpers.ParseQuery(new Uri(sent.Data["confirmationLink"]).Fragment.TrimStart('#'))["token"].ToString();

        Assert.Equal(AccountDeletionConfirmationOutcome.Confirmed,
            await service.ConfirmAsync(token, CancellationToken.None));
        Assert.Equal(AccountDeletionConfirmationOutcome.InvalidOrExpiredToken,
            await service.ConfirmAsync(token, CancellationToken.None));

        db.ChangeTracker.Clear();
        var checkout = new PaymentCheckoutService(db, new FakePaymentGateway("https://checkout.example.test/"),
            new PaymentAccountEligibilityReader(db), new PaymentInvitationEligibilityReader(db),
            new PaymentPlanCatalogReader(db), new AccountQuotaTransactionRunner(db),
            Options.Create(new PublicWebOptions { BaseUrl = "https://example.test" }), new FrozenClock(Now));
        var deletedCheckout = await checkout.StartAsync(account.Id,
            new StartPaymentCheckoutRequest(invitationIds[0], "standard", "deleted-checkout-key-0001"),
            CancellationToken.None);
        Assert.Equal(PaymentCheckoutOutcome.NotEligible, deletedCheckout.Outcome);
        Assert.Empty(await db.PaymentAttempts.Where(item => item.AccountId == account.Id).ToListAsync());

        var persistedAccount = await db.Accounts.SingleAsync(item => item.Id == account.Id);
        Assert.NotNull(persistedAccount.DeletionStartedAtUtc);
        Assert.NotNull(persistedAccount.DeletionCompletedAtUtc);
        Assert.Equal("Deleted account", persistedAccount.DisplayName);
        var identity = await db.Users.SingleAsync(item => item.Id == identityUserId);
        Assert.Equal($"deleted-{identityUserId:N}@invalid.local", identity.Email);
        Assert.False(identity.EmailConfirmed);
        Assert.Null(identity.PhoneNumber);
        Assert.Empty(await db.UserLogins.Where(item => item.UserId == identityUserId).ToListAsync());
        Assert.Empty(await db.UserClaims.Where(item => item.UserId == identityUserId).ToListAsync());
        Assert.Empty(await db.UserTokens.Where(item => item.UserId == identityUserId).ToListAsync());
        var invitations = await db.Invitations.IgnoreQueryFilters().Where(item => invitationIds.Contains(item.Id)).ToListAsync();
        Assert.Equal(2, invitations.Count);
        Assert.All(invitations, invitation =>
        {
            Assert.Equal(Now, invitation.DeletedAt);
            Assert.Equal(Now, invitation.PurgeAfter);
        });
        Assert.Equal(AccountDeletionRequestStatus.Consumed,
            (await db.AccountDeletionRequests.SingleAsync()).Status);
        var work = await db.AccountDeletionWorks.SingleAsync();
        Assert.Equal(AccountDeletionWorkStatus.Completed, work.Status);
        Assert.NotNull(work.InvitationsPurgeQueuedAtUtc);
        Assert.NotNull(work.IdentitySanitizedAtUtc);
        Assert.True(await new AccountDeletionStatusReader(db).IsDeletingAsync(account.Id, CancellationToken.None));
        var accountValidator = new AccountReferenceValidator(db);
        Assert.Equal(AccountReferenceStatus.Deleting,
            await accountValidator.GetStatusAsync(account.Id, CancellationToken.None));
        var invitation = await db.Invitations.IgnoreQueryFilters().SingleAsync(item => item.Id == invitationIds[0]);
        var publicInvitationService = new Davetiye.Infrastructure.Modules.Invitations.PublicInvitationService(
            db, accountValidator, null!, null!, null!, new FrozenClock(Now));
        Assert.Equal(PublicInvitationOutcome.NotFound,
            (await publicInvitationService.GetAsync(invitation.PublicCode, CancellationToken.None)).Outcome);
        var mediaLibrary = new CreatorMediaLibraryService(db, accountValidator, new UnusedTemplateResolver(),
            new OwnedInvitationReader(), new FrozenClock(Now));
        Assert.Equal("NotFound", (await mediaLibrary.ListAsync(account.Id, invitation.Id, CancellationToken.None)).Outcome);

        var subscriptionReader = new OrganizationSubscriptionEntitlementReader(db);
        var lifecycle = new InvitationLifecycleJobs(db, new AccountQuotaTransactionRunner(db),
            new PublicationGrantLifecycleService(db, null!, new FrozenClock(Now)),
            new PublicationGrantAccessValidator(db, subscriptionReader),
            new MediaPurgeCoordinator(db, new OutboxWorkStore(db), new FrozenClock(Now)),
            new RsvpPurgeCoordinator(db), new MemoriesPurgeCoordinator(db), new GiftRegistryPurgeCoordinator(db),
            new FrozenClock(Now), NullLogger<InvitationLifecycleJobs>.Instance);
        var purge = await lifecycle.RunBatchAsync(100, CancellationToken.None);
        Assert.Equal(2, purge.Purged);
        Assert.Empty(await db.Invitations.IgnoreQueryFilters().Where(item => invitationIds.Contains(item.Id)).ToListAsync());
        var replayedPurge = await lifecycle.RunBatchAsync(100, CancellationToken.None);
        Assert.Equal(0, replayedPurge.Purged);
    }

    [Fact]
    public async Task Expired_confirmation_token_cannot_start_deletion()
    {
        var (_, db, account, _, _) = await SeedAsync();
        var clock = new MutableClock(Now);
        var mail = new RecordingEmailSender();
        var service = CreateService(db, mail, clock);
        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await service.RequestAsync(account.Id, CancellationToken.None));
        var sent = Assert.Single(mail.Messages);
        var token = QueryHelpers.ParseQuery(new Uri(sent.Data["confirmationLink"]).Fragment.TrimStart('#'))["token"].ToString();

        clock.UtcNow = Now.AddMinutes(61);
        Assert.Equal(AccountDeletionConfirmationOutcome.InvalidOrExpiredToken,
            await service.ConfirmAsync(token, CancellationToken.None));
        Assert.Null((await db.Accounts.SingleAsync(item => item.Id == account.Id)).DeletionStartedAtUtc);
        Assert.Equal(AccountDeletionRequestStatus.Expired, (await db.AccountDeletionRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Concurrent_confirmation_requests_are_serialized_and_only_one_consumes_the_token()
    {
        var (connectionString, db1, account, _, _) = await SeedAsync();
        var mail = new RecordingEmailSender();
        var service1 = CreateService(db1, mail, new FrozenClock(Now));
        Assert.Equal(AccountDeletionRequestOutcome.Accepted,
            await service1.RequestAsync(account.Id, CancellationToken.None));
        var token = QueryHelpers.ParseQuery(new Uri(Assert.Single(mail.Messages).Data["confirmationLink"])
            .Fragment.TrimStart('#'))["token"].ToString();

        await using var db2 = CreateDbContext(connectionString);
        var service2 = CreateService(db2, new RecordingEmailSender(), new FrozenClock(Now));
        var outcomes = await Task.WhenAll(
            service1.ConfirmAsync(token, CancellationToken.None),
            service2.ConfirmAsync(token, CancellationToken.None));

        Assert.Single(outcomes, outcome => outcome == AccountDeletionConfirmationOutcome.Confirmed);
        Assert.Single(outcomes, outcome => outcome == AccountDeletionConfirmationOutcome.InvalidOrExpiredToken);
        db1.ChangeTracker.Clear();
        Assert.Single(await db1.AccountDeletionWorks.ToListAsync());
    }

    [Fact]
    public async Task Deletion_immediately_stops_local_organization_renewal_and_keeps_provider_intent_pending()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(CancellationToken.None);
        var plan = await db.Plans.SingleAsync(item => item.Key == "organization");
        var userId = Guid.NewGuid();
        var account = Account.Create(Guid.NewGuid(), userId, AccountType.Organization, "Organization", Now);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"org-delete-{userId:N}",
            NormalizedUserName = $"ORG-DELETE-{userId:N}".ToUpperInvariant(),
            Email = $"org-delete-{userId:N}@example.test",
            NormalizedEmail = $"ORG-DELETE-{userId:N}@EXAMPLE.TEST".ToUpperInvariant(),
            EmailConfirmed = true
        });
        db.Accounts.Add(account);
        var subscription = OrganizationSubscription.ActivateFromVerifiedInitialPayment(
            Guid.NewGuid(), account.Id, plan.Id, plan.PriceAmount, "test-provider", "provider-sub-delete",
            "cycle-initial", Now, Now.AddDays(30), Now);
        db.OrganizationSubscriptions.Add(subscription);
        db.AccountPlanGrants.Add(AccountPlanGrant.Create(Guid.NewGuid(), account.Id, plan.Id,
            GrantSource.OrganizationSubscription, Now));
        await db.SaveChangesAsync();

        var mail = new RecordingEmailSender();
        var deletion = CreateService(db, mail, new FrozenClock(Now));
        Assert.Equal(AccountDeletionRequestOutcome.Accepted, await deletion.RequestAsync(account.Id, CancellationToken.None));
        var token = QueryHelpers.ParseQuery(new Uri(Assert.Single(mail.Messages).Data["confirmationLink"])
            .Fragment.TrimStart('#'))["token"].ToString();
        Assert.Equal(AccountDeletionConfirmationOutcome.Confirmed, await deletion.ConfirmAsync(token, CancellationToken.None));

        db.ChangeTracker.Clear();
        var stored = await db.OrganizationSubscriptions.SingleAsync(item => item.Id == subscription.Id);
        Assert.Equal(Now, stored.RenewalCancellationRequestedAtUtc);
        Assert.Null(stored.RenewalCancellationCompletedAtUtc);
        Assert.True(stored.CancelAtPeriodEnd);
        var intent = await db.OutboxMessages.SingleAsync(item => item.MessageType == "payments.organization-renewal-cancellation");
        Assert.Equal(account.Id, intent.OwnerAccountId);
        Assert.Null(intent.ProcessedAt);
        var revokedGrant = await db.AccountPlanGrants.SingleAsync(item => item.AccountId == account.Id);
        Assert.Equal(Now, revokedGrant.RevokedAt);

        var lifecycle = new OrganizationSubscriptionLifecycleStore(db, new AccountQuotaTransactionRunner(db),
            new NullEmailSender());
        var lateRenewal = await lifecycle.ApplyVerifiedRenewalAsync(new VerifiedOrganizationSubscriptionRenewal(
            "test-provider", "provider-sub-delete", "late-cycle", Now.AddDays(30), Now.AddDays(61),
            OrganizationSubscriptionRenewalOutcome.Succeeded), Now.AddDays(31), CancellationToken.None);
        Assert.Equal(OrganizationSubscriptionCommandOutcome.Canceled, lateRenewal.Outcome);
        db.ChangeTracker.Clear();
        stored = await db.OrganizationSubscriptions.SingleAsync(item => item.Id == subscription.Id);
        Assert.Equal(Now.AddDays(30), stored.PaidThroughAtUtc);
        Assert.Null(stored.RenewalCancellationCompletedAtUtc);
        Assert.Null(intent.ProcessedAt);
    }

    private async Task<(string ConnectionString, DavetiyeDbContext Db, Account Account, Guid UserId, Guid[] InvitationIds)> SeedAsync()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        var userId = Guid.NewGuid();
        var account = Account.Create(Guid.NewGuid(), userId, AccountType.Individual, "Deletion account", Now);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"deletion-{userId:N}",
            NormalizedUserName = $"DELETION-{userId:N}",
            Email = $"deletion-{userId:N}@example.test",
            NormalizedEmail = $"DELETION-{userId:N}@EXAMPLE.TEST",
            EmailConfirmed = true,
            PhoneNumber = "+15555550100"
        });
        db.Accounts.Add(account);
        var invitations = Enumerable.Range(0, 2).Select(_ => Invitation.Create(Guid.NewGuid(), account.Id,
            (Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray())).ToLowerInvariant(), Now)).ToArray();
        db.Invitations.AddRange(invitations);
        await db.SaveChangesAsync();
        return (connectionString, db, account, userId, invitations.Select(item => item.Id).ToArray());
    }

    private static AccountDeletionService CreateService(DavetiyeDbContext db, RecordingEmailSender sender, IClock clock) =>
        new(db, new AccountQuotaTransactionRunner(db),
            new OrganizationSubscriptionAccountDeletionHandler(db, new OutboxWorkStore(db)),
            new InvitationAccountDeletionHandler(db), new AccountPlanGrantDeletionHandler(db), sender, clock,
            Options.Create(new EmailTokenOptions { TokenLifetimeMinutes = 60 }),
            Options.Create(new PublicWebOptions { BaseUrl = "https://example.test" }));

    private static DavetiyeDbContext CreateDbContext(string connectionString) => new(
        new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connectionString,
            options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

    private sealed class FrozenClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }

    private sealed class MutableClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<SentMessage> Messages { get; } = [];
        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task SendShortLivedForAccountAsync(Guid ownerAccountId, string toEmail, string kind,
            IReadOnlyDictionary<string, string> data, DateTimeOffset protectUntilUtc, CancellationToken cancellationToken)
        {
            Messages.Add(new(ownerAccountId, toEmail, kind, data, protectUntilUtc));
            return Task.CompletedTask;
        }
    }

    private sealed class NullEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string kind, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedTemplateResolver : ITemplateSelectionResolver
    {
        public Task<TemplateSelection?> ResolveActiveAsync(string templateKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deleted account should fail before template lookup.");
    }

    private sealed class OwnedInvitationReader : ICreatorMediaInvitationOwnerReader
    {
        public Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deleted account should fail before ownership lookup.");
        public Task<string?> GetOwnedTemplateKeyAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deleted account should fail before ownership lookup.");
    }

    private sealed record SentMessage(Guid OwnerAccountId, string ToEmail, string Kind,
        IReadOnlyDictionary<string, string> Data, DateTimeOffset ProtectUntilUtc);
}
