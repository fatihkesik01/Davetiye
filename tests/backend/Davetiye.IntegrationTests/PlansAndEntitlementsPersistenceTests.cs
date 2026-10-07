using System.Data.Common;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PlansAndEntitlementsPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task Payment_plan_adapter_maps_plan_catalog_billing_kind_to_payments_owned_kind()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        await new PlanCatalogInitializer(context).InitializeAsync(CancellationToken.None);

        var reader = new PaymentPlanCatalogReader(context);
        var plan = await reader.FindIndividualPlanAsync("standard", CancellationToken.None);
        var plans = await reader.ListIndividualPlansAsync(CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal(PurchasableBillingKind.OneTime, plan.BillingKind);
        Assert.Collection(plans,
            item => Assert.Equal("standard", item.Key),
            item => Assert.Equal("premium", item.Key));
    }

    [Fact]
    public async Task Product_catalog_seed_is_atomic_idempotent_and_preserves_operator_changes()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var initializer = new PlanCatalogInitializer(context);
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Equal(4, await context.Plans.CountAsync());
        Assert.Equal(4 * EntitlementCatalog.All.Count, await context.PlanEntitlements.CountAsync());

        var plans = await context.Plans.OrderBy(plan => plan.Key).ToListAsync();
        Assert.Collection(
            plans,
            plan => AssertPlan(plan, "free", 0m, PlanBillingKind.Free),
            plan => AssertPlan(plan, "organization", 2_499m, PlanBillingKind.Monthly),
            plan => AssertPlan(plan, "premium", 1_199m, PlanBillingKind.OneTime),
            plan => AssertPlan(plan, "standard", 699m, PlanBillingKind.OneTime));

        foreach (var plan in plans)
        {
            var keys = await context.PlanEntitlements
                .Where(entitlement => entitlement.PlanId == plan.Id)
                .Select(entitlement => entitlement.EntitlementKey)
                .ToListAsync();
            Assert.Equal(
                EntitlementCatalog.All.Select(definition => definition.Key).Order(),
                keys.Order());
        }

        var standard = plans.Single(plan => plan.Key == "standard");
        standard.UpdateCommercialTerms(777m, "TRY", PlanBillingKind.OneTime);
        standard.UpdateDescription("  Operator description  ");
        var publishDays = await context.PlanEntitlements.SingleAsync(entitlement =>
            entitlement.PlanId == standard.Id &&
            entitlement.EntitlementKey == EntitlementCatalog.MaxPublishDays);
        publishDays.UpdateValue(42, null);
        await context.SaveChangesAsync();

        await initializer.InitializeAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        standard = await context.Plans.SingleAsync(plan => plan.Key == "standard");
        publishDays = await context.PlanEntitlements.SingleAsync(entitlement =>
            entitlement.PlanId == standard.Id &&
            entitlement.EntitlementKey == EntitlementCatalog.MaxPublishDays);
        Assert.Equal(777m, standard.PriceAmount);
        Assert.Equal("Operator description", standard.Description);
        Assert.Equal(42, publishDays.NumericValue);
    }

    [Fact]
    public async Task Migration_backfills_known_commercial_terms_and_deactivates_unknown_plans()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync("20260929200313_P2M2_InvitationsAndTemplates");

        var freeId = Guid.NewGuid();
        var standardId = Guid.NewGuid();
        var premiumId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plans (id, key, display_name, is_active, revision)
            VALUES
                ({freeId}, 'free', 'Operator Free', true, 0),
                ({standardId}, 'standard', 'Operator Standard', false, 0),
                ({premiumId}, 'premium', 'Operator Premium', true, 0),
                ({organizationId}, 'organization', 'Operator Organization', true, 0),
                ({unknownId}, 'legacy-special', 'Legacy Special', true, 0)
            """);

        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();

        var plans = await context.Plans
            .Where(candidate =>
                candidate.Id == freeId ||
                candidate.Id == standardId ||
                candidate.Id == premiumId ||
                candidate.Id == organizationId ||
                candidate.Id == unknownId)
            .ToDictionaryAsync(candidate => candidate.Key);

        AssertUpgradedPlan(plans["free"], "Operator Free", 0m, PlanBillingKind.Free, isActive: true);
        AssertUpgradedPlan(plans["standard"], "Operator Standard", 699m, PlanBillingKind.OneTime, isActive: false);
        AssertUpgradedPlan(plans["premium"], "Operator Premium", 1_199m, PlanBillingKind.OneTime, isActive: true);
        AssertUpgradedPlan(plans["organization"], "Operator Organization", 2_499m, PlanBillingKind.Monthly, isActive: true);
        AssertUpgradedPlan(plans["legacy-special"], "Legacy Special", 0m, PlanBillingKind.OneTime, isActive: false);
    }

    [Fact]
    public async Task Database_allows_only_one_lifetime_free_grant_per_account()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        Guid freePlanId;
        await using (var seedContext = CreateDbContext(connectionString))
        {
            freePlanId = await seedContext.Plans
                .Where(plan => plan.Key == "free")
                .Select(plan => plan.Id)
                .SingleAsync();
            seedContext.AccountPlanGrants.Add(AccountPlanGrant.Create(
                Guid.NewGuid(), Guid.Parse("10000000-0000-0000-0000-000000000001"),
                freePlanId, GrantSource.Free, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        await using var secondContext = CreateDbContext(connectionString);
        secondContext.AccountPlanGrants.Add(AccountPlanGrant.Create(
            Guid.NewGuid(), Guid.Parse("10000000-0000-0000-0000-000000000001"),
            freePlanId, GrantSource.Free, DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => secondContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("ux_account_plan_grants_lifetime_free_per_account", postgresException.ConstraintName);
    }

    [Fact]
    public async Task Concurrent_free_reservations_for_different_invitations_have_exactly_one_winner()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var accountId = Guid.NewGuid();
        var firstInvitationId = Guid.NewGuid();
        var secondInvitationId = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var now = DateTimeOffset.UtcNow;
        var instant = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));

        async Task<FreeGrantReservationResult> ReserveAsync(Guid invitationId)
        {
            await start.Task;
            await using var context = CreateDbContext(connectionString);
            var runner = new AccountQuotaTransactionRunner(context);
            var store = new FreeGrantReservationStore(context, runner);
            return await store.TryReserveAsync(accountId, invitationId, instant, CancellationToken.None);
        }

        var first = ReserveAsync(firstInvitationId);
        var second = ReserveAsync(secondInvitationId);
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Outcome == FreeGrantReservationOutcome.Reserved);
        Assert.Single(results, result =>
            result.Outcome == FreeGrantReservationOutcome.ReservedForAnotherInvitation);

        await using var verificationContext = CreateDbContext(connectionString);
        var grant = await verificationContext.AccountPlanGrants.SingleAsync(candidate =>
            candidate.AccountId == accountId && candidate.Source == GrantSource.Free);
        Assert.True(
            grant.AssignedInvitationId == firstInvitationId ||
            grant.AssignedInvitationId == secondInvitationId);
        Assert.Equal(instant, grant.ReservedAt);
        Assert.Null(grant.ConsumedAt);
    }

    [Fact]
    public async Task Free_reservation_fails_closed_when_existing_free_source_points_to_another_plan()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var accountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            var standardPlanId = await seedContext.Plans
                .Where(plan => plan.Key == "standard")
                .Select(plan => plan.Id)
                .SingleAsync();
            seedContext.AccountPlanGrants.Add(AccountPlanGrant.Create(
                Guid.NewGuid(), accountId, standardPlanId, GrantSource.Free, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateDbContext(connectionString);
        var store = new FreeGrantReservationStore(context, new AccountQuotaTransactionRunner(context));
        var result = await store.TryReserveAsync(
            accountId, invitationId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.InvalidConfiguration, result.Outcome);
        context.ChangeTracker.Clear();
        var grant = await context.AccountPlanGrants.SingleAsync(candidate => candidate.AccountId == accountId);
        Assert.Null(grant.AssignedInvitationId);
        Assert.Null(grant.ReservedAt);
    }

    [Fact]
    public async Task Initial_free_admission_fails_closed_for_free_key_with_non_free_billing()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var newAccountId = Guid.NewGuid();
        var existingGrantAccountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            var freePlan = await seedContext.Plans.SingleAsync(plan => plan.Key == "free");
            freePlan.UpdateCommercialTerms(0m, "TRY", PlanBillingKind.OneTime);
            seedContext.AccountPlanGrants.Add(AccountPlanGrant.Create(
                Guid.NewGuid(),
                existingGrantAccountId,
                freePlan.Id,
                GrantSource.Free,
                DateTimeOffset.UtcNow.AddMinutes(-1)));
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateDbContext(connectionString);
        var store = new FreeGrantReservationStore(context, new AccountQuotaTransactionRunner(context));

        var reserveResult = await store.TryReserveAsync(
            newAccountId,
            invitationId,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var consumeResult = await store.TryConsumeAsync(
            existingGrantAccountId,
            invitationId,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.InvalidConfiguration, reserveResult.Outcome);
        Assert.Equal(FreeGrantReservationOutcome.InvalidConfiguration, consumeResult.Outcome);
        context.ChangeTracker.Clear();
        Assert.False(await context.AccountPlanGrants.AnyAsync(grant => grant.AccountId == newAccountId));
        var existingGrant = await context.AccountPlanGrants.SingleAsync(
            grant => grant.AccountId == existingGrantAccountId);
        Assert.Null(existingGrant.AssignedInvitationId);
        Assert.Null(existingGrant.ReservedAt);
        Assert.Null(existingGrant.ConsumedAt);
    }

    [Fact]
    public async Task Scheduled_consumption_never_creates_a_missing_reservation()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var accountId = Guid.NewGuid();
        await using var context = CreateDbContext(connectionString);
        var store = new FreeGrantReservationStore(context, new AccountQuotaTransactionRunner(context));

        var result = await store.TryConsumeExistingReservationAsync(
            accountId, Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(FreeGrantReservationOutcome.ReservationNotFound, result.Outcome);
        Assert.False(await context.AccountPlanGrants.AnyAsync(grant => grant.AccountId == accountId));
    }

    [Fact]
    public async Task Accepted_scheduled_reservation_remains_consumable_after_plan_deactivation()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var accountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var reservedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await using (var reservationContext = CreateDbContext(connectionString))
        {
            var store = new FreeGrantReservationStore(
                reservationContext,
                new AccountQuotaTransactionRunner(reservationContext));
            var reservation = await store.TryReserveAsync(
                accountId, invitationId, reservedAt, CancellationToken.None);
            Assert.True(reservation.IsSuccess);
        }

        await using (var adminContext = CreateDbContext(connectionString))
        {
            var freePlan = await adminContext.Plans.SingleAsync(plan => plan.Key == "free");
            freePlan.SetActive(false);
            await adminContext.SaveChangesAsync();
        }

        await using var consumptionContext = CreateDbContext(connectionString);
        var consumptionStore = new FreeGrantReservationStore(
            consumptionContext,
            new AccountQuotaTransactionRunner(consumptionContext));
        var result = await consumptionStore.TryConsumeExistingReservationAsync(
            accountId, invitationId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var grant = await consumptionContext.AccountPlanGrants.SingleAsync(candidate => candidate.Id == result.GrantId);
        Assert.NotNull(grant.ConsumedAt);
    }

    [Fact]
    public async Task Entitlement_reader_is_account_scoped_and_returns_all_typed_values()
    {
        var connectionString = await CreateSeededDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var grantId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(connectionString))
        {
            var premiumPlanId = await seedContext.Plans
                .Where(plan => plan.Key == "premium")
                .Select(plan => plan.Id)
                .SingleAsync();
            seedContext.AccountPlanGrants.Add(AccountPlanGrant.Create(
                grantId, ownerId, premiumPlanId, GrantSource.IndividualPurchase, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        var commandCounter = new ReaderCommandCounter();
        await using var context = CreateDbContext(connectionString, commandCounter);
        var reader = new EntitlementGrantReader(context, new OrganizationSubscriptionEntitlementReader(context));
        Assert.Null(await reader.FindOwnedGrantAsync(Guid.NewGuid(), grantId, CancellationToken.None));

        commandCounter.Reset();
        var snapshot = await reader.FindOwnedGrantAsync(ownerId, grantId, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("premium", snapshot.PlanKey);
        Assert.Equal(PlanBillingKind.OneTime, snapshot.PlanBillingKind);
        Assert.Equal(EntitlementCatalog.All.Count, snapshot.Values.Count);
        Assert.Equal(1, commandCounter.ReaderCommandCount);
    }

    [Fact]
    public async Task Invitation_ownership_validator_does_not_disclose_foreign_or_missing_invitations()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var ownerAccountId = Guid.NewGuid();
        var foreignAccountId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        context.Invitations.Add(Invitation.Create(
            invitationId,
            ownerAccountId,
            new string('a', PublicInvitationCode.EncodedLength),
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var validator = new InvitationOwnershipValidator(context);
        Assert.True(await validator.IsOwnedByAccountAsync(
            ownerAccountId, invitationId, CancellationToken.None));
        Assert.False(await validator.IsOwnedByAccountAsync(
            foreignAccountId, invitationId, CancellationToken.None));
        Assert.False(await validator.IsOwnedByAccountAsync(
            ownerAccountId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Account_reference_validator_rejects_active_ban_but_ignores_revoked_ban()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();

        var activeBanAccountId = Guid.NewGuid();
        var revokedBanAccountId = Guid.NewGuid();
        AddVerifiedAccount(context, activeBanAccountId);
        AddVerifiedAccount(context, revokedBanAccountId);

        context.BanRecords.Add(BanRecord.Create(
            Guid.NewGuid(), activeBanAccountId, "Active integration-test ban",
            DateTimeOffset.UtcNow, Guid.NewGuid()));
        var revokedBan = BanRecord.Create(
            Guid.NewGuid(), revokedBanAccountId, "Revoked integration-test ban",
            DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid());
        revokedBan.Revoke(DateTimeOffset.UtcNow);
        context.BanRecords.Add(revokedBan);
        await context.SaveChangesAsync();

        var validator = new AccountReferenceValidator(context);
        Assert.Equal(
            AccountReferenceStatus.Banned,
            await validator.GetStatusAsync(activeBanAccountId, CancellationToken.None));
        Assert.Equal(
            AccountReferenceStatus.Verified,
            await validator.GetStatusAsync(revokedBanAccountId, CancellationToken.None));
        Assert.Equal(
            AccountReferenceStatus.NotFound,
            await validator.GetStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private async Task<string> CreateSeededDatabaseAsync()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        await new PlanCatalogInitializer(context).InitializeAsync(CancellationToken.None);
        return connectionString;
    }

    private static void AssertPlan(
        Plan plan,
        string key,
        decimal priceAmount,
        PlanBillingKind billingKind)
    {
        Assert.Equal(key, plan.Key);
        Assert.Equal(priceAmount, plan.PriceAmount);
        Assert.Equal("TRY", plan.Currency);
        Assert.Equal(billingKind, plan.BillingKind);
        Assert.True(plan.IsActive);
    }

    private static void AssertUpgradedPlan(
        Plan plan,
        string displayName,
        decimal priceAmount,
        PlanBillingKind billingKind,
        bool isActive)
    {
        Assert.Equal(displayName, plan.DisplayName);
        Assert.Equal(priceAmount, plan.PriceAmount);
        Assert.Equal("TRY", plan.Currency);
        Assert.Equal(billingKind, plan.BillingKind);
        Assert.Equal(isActive, plan.IsActive);
    }

    private static void AddVerifiedAccount(DavetiyeDbContext context, Guid accountId)
    {
        var identityUserId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = identityUserId,
            UserName = $"user-{identityUserId:N}",
            NormalizedUserName = $"USER-{identityUserId:N}",
            Email = $"{identityUserId:N}@example.test",
            NormalizedEmail = $"{identityUserId:N}@EXAMPLE.TEST",
            EmailConfirmed = true
        });
        context.Accounts.Add(Account.Create(
            accountId,
            identityUserId,
            AccountType.Individual,
            $"Account {accountId:N}",
            DateTimeOffset.UtcNow));
    }

    private static DavetiyeDbContext CreateDbContext(
        string connectionString,
        params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .AddInterceptors(interceptors)
            .Options);

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public void Reset() => ReaderCommandCount = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Guest_media_entitlements_are_seeded_with_accepted_values_in_every_plan_and_backfilled_without_overwriting_operator_values()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync();
        var initializer = new PlanCatalogInitializer(context);
        await initializer.InitializeAsync(CancellationToken.None);

        var expected = new Dictionary<string, long>
        {
            [EntitlementCatalog.MaxGuestImages] = 100,
            [EntitlementCatalog.MaxGuestVideos] = 10,
            [EntitlementCatalog.MaxGuestImageSizeMb] = 10,
            [EntitlementCatalog.MaxGuestVideoSizeMb] = 100,
            [EntitlementCatalog.MaxGuestVideoDurationSeconds] = 60,
        };
        foreach (var plan in await context.Plans.ToListAsync())
        {
            foreach (var (key, value) in expected)
            {
                var row = await context.PlanEntitlements.SingleAsync(item => item.PlanId == plan.Id && item.EntitlementKey == key);
                Assert.Equal(value, row.NumericValue);
            }
        }

        // An existing deployment (rows predate the guest keys) is backfilled by the idempotent seed; operator edits survive.
        var standard = await context.Plans.SingleAsync(plan => plan.Key == "standard");
        context.PlanEntitlements.RemoveRange(await context.PlanEntitlements
            .Where(item => item.PlanId == standard.Id && expected.Keys.Contains(item.EntitlementKey)).ToListAsync());
        var premium = await context.Plans.SingleAsync(plan => plan.Key == "premium");
        (await context.PlanEntitlements.SingleAsync(item => item.PlanId == premium.Id &&
            item.EntitlementKey == EntitlementCatalog.MaxGuestImages)).UpdateValue(42, null);
        await context.SaveChangesAsync();

        await initializer.InitializeAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        foreach (var (key, value) in expected)
            Assert.Equal(value, (await context.PlanEntitlements.SingleAsync(item => item.PlanId == standard.Id && item.EntitlementKey == key)).NumericValue);
        Assert.Equal(42, (await context.PlanEntitlements.SingleAsync(item => item.PlanId == premium.Id &&
            item.EntitlementKey == EntitlementCatalog.MaxGuestImages)).NumericValue);
    }
}
