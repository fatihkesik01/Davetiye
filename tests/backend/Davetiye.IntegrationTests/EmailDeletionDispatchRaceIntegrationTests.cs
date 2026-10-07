using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.Notifications;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EmailDeletionDispatchRaceIntegrationTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly IDataProtectionProvider DataProtectionProvider = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Deletion_lock_wins_and_owned_email_is_suppressed_without_transport()
    {
        var setup = await SeedAsync();
        await using var lockDb = CreateDbContext(setup.ConnectionString);
        var runner = new AccountQuotaTransactionRunner(lockDb);
        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletion = runner.ExecuteAndCommitAsync(setup.AccountId, async token =>
        {
            var account = await lockDb.Accounts.SingleAsync(item => item.Id == setup.AccountId, token);
            account.BeginDeletion(Now);
            await lockDb.SaveChangesAsync(token);
            lockAcquired.SetResult();
            await releaseLock.Task.WaitAsync(token);
            return true;
        }, CancellationToken.None);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var transport = new ControlledTransport();
        using var provider = BuildWorkerProvider(setup.ConnectionString, transport);
        var worker = CreateWorker(provider);
        var processing = worker.ProcessBatchAsync(CancellationToken.None);
        await WaitForClaimAsync(setup.ConnectionString, setup.MessageId);
        releaseLock.SetResult();
        await deletion.WaitAsync(TimeSpan.FromSeconds(10));
        await processing.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, transport.CallCount);
        await using var verify = CreateDbContext(setup.ConnectionString);
        var message = await verify.OutboxMessages.SingleAsync(item => item.Id == setup.MessageId);
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.DispatchStartedAtUtc);
    }

    [Fact]
    public async Task Legacy_ownerless_email_is_quarantined_without_transport()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        var messageId = Guid.NewGuid();
        await using (var db = CreateDbContext(connectionString))
        {
            // Model the deployed database immediately before owner_account_id was introduced.
            await db.Database.MigrateAsync("20261006192152_P10M4AccountConsentRecords");
            var legacyPayload = new EmailOutboxPayloadProtector(DataProtectionProvider).Protect(
                "legacy@example.test", EmailNotificationKinds.InvitationPublished,
                new Dictionary<string, string> { ["invitationTitle"] = "Legacy message" }, null);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO outbox_messages
                    (id, message_type, payload, created_at, processed_at, attempt_count,
                     next_attempt_at, claimed_until, failed_permanently, revision)
                VALUES
                    ({messageId}, {EmailOutboxWorker.MessageType}, {legacyPayload}, {Now}, NULL,
                     0, {Now}, NULL, FALSE, 0)
                """);

            // The nullable account-owner column is added by this upgrade and remains null for
            // preexisting rows; the worker must quarantine rather than dispatch such a row.
            await db.Database.MigrateAsync();
        }

        var transport = new ControlledTransport();
        using var provider = BuildWorkerProvider(connectionString, transport);
        var worker = CreateWorker(provider);
        Assert.Equal(1, await worker.ProcessBatchAsync(CancellationToken.None));

        Assert.Equal(0, transport.CallCount);
        await using var verify = CreateDbContext(connectionString);
        var quarantined = await verify.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == messageId);
        Assert.Null(quarantined.ProcessedAt);
        Assert.True(quarantined.FailedPermanently);
        Assert.Null(quarantined.DispatchStartedAtUtc);
        Assert.Null(quarantined.ClaimedUntil);
    }

    [Fact]
    public async Task Dispatch_marker_wins_before_transport_and_deletion_does_not_hold_or_recall_in_flight_send()
    {
        var setup = await SeedAsync();
        var transport = new ControlledTransport(block: true);
        using var provider = BuildWorkerProvider(setup.ConnectionString, transport);
        var worker = CreateWorker(provider);
        var processing = worker.ProcessBatchAsync(CancellationToken.None);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var verifyMarker = CreateDbContext(setup.ConnectionString);
        var beforeDeletion = await verifyMarker.OutboxMessages.AsNoTracking()
            .SingleAsync(item => item.Id == setup.MessageId);
        Assert.NotNull(beforeDeletion.DispatchStartedAtUtc);
        Assert.Null(beforeDeletion.ProcessedAt);

        await using var deletionDb = CreateDbContext(setup.ConnectionString);
        var runner = new AccountQuotaTransactionRunner(deletionDb);
        await runner.ExecuteAndCommitAsync(setup.AccountId, async token =>
        {
            var account = await deletionDb.Accounts.SingleAsync(item => item.Id == setup.AccountId, token);
            account.BeginDeletion(Now.AddSeconds(1));
            await deletionDb.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        transport.Release.SetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = CreateDbContext(setup.ConnectionString);
        var message = await verify.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == setup.MessageId);
        Assert.Equal(1, transport.CallCount);
        Assert.NotNull(message.DispatchStartedAtUtc);
        Assert.NotNull(message.ProcessedAt);
        Assert.True(message.DispatchStartedAtUtc <= message.ProcessedAt);
    }

    private async Task<SeededMessage> SeedAsync()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        var userId = Guid.NewGuid();
        var account = Account.Create(Guid.NewGuid(), userId, AccountType.Individual, "Email race", Now);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"email-race-{userId:N}",
            NormalizedUserName = $"EMAIL-RACE-{userId:N}".ToUpperInvariant(),
            Email = $"email-race-{userId:N}@example.test",
            NormalizedEmail = $"EMAIL-RACE-{userId:N}@EXAMPLE.TEST".ToUpperInvariant(),
            EmailConfirmed = true
        });
        db.Accounts.Add(account);
        var messageId = Guid.NewGuid();
        var payload = new EmailOutboxPayloadProtector(DataProtectionProvider).Protect(
            $"email-race-{userId:N}@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "Race test" }, null);
        db.OutboxMessages.Add(OutboxMessage.Create(messageId, EmailOutboxWorker.MessageType,
            payload, Now, account.Id));
        await db.SaveChangesAsync();
        return new(connectionString, account.Id, messageId);
    }

    private static ServiceProvider BuildWorkerProvider(string connectionString, ControlledTransport transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CreateDbContext(connectionString));
        services.AddScoped<IOutboxWorkStore, OutboxWorkStore>();
        services.AddScoped<AccountQuotaTransactionRunner>();
        services.AddScoped<IOutermostAccountQuotaTransactionRunner>(sp => sp.GetRequiredService<AccountQuotaTransactionRunner>());
        services.AddScoped<IAccountDeletionStatusReader, AccountDeletionStatusReader>();
        services.AddScoped<IEmailOwnedDispatchGate, AccountOwnedEmailDispatchGate>();
        services.AddSingleton<EmailOutboxPayloadProtector>(
            new EmailOutboxPayloadProtector(DataProtectionProvider));
        services.AddSingleton<EmailTemplateRenderer>();
        services.AddSingleton<IEmailTransport>(transport);
        return services.BuildServiceProvider();
    }

    private static EmailOutboxWorker CreateWorker(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new EmailOutboxOptions
        {
            BatchSize = 1,
            PollIntervalSeconds = 1,
            LeaseSeconds = 120,
            MaximumDeliveryAgeHours = 24
        }), new FrozenClock(Now), NullLogger<EmailOutboxWorker>.Instance);

    private static async Task WaitForClaimAsync(string connectionString, Guid messageId)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < until)
        {
            await using var db = CreateDbContext(connectionString);
            if (await db.OutboxMessages.AsNoTracking().AnyAsync(item => item.Id == messageId && item.ClaimedUntil != null))
                return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Worker did not claim the test email.");
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString) => new(
        new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connectionString,
            options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);

    private sealed record SeededMessage(string ConnectionString, Guid AccountId, Guid MessageId);
    private sealed class FrozenClock(DateTimeOffset at) : IClock { public DateTimeOffset UtcNow { get; } = at; }

    private sealed class ControlledTransport(bool block = false) : IEmailTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public async Task SendAsync(EmailDeliveryMessage message, Guid idempotencyId, CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            if (block) await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
