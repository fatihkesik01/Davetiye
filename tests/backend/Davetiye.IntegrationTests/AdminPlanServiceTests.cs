using System.Diagnostics;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminPlanServiceTests(PostgreSqlFixture postgres)
{
    private sealed class AdminPlanApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test"
            }));
        }
    }

    [Fact]
    public async Task Admin_update_is_typed_revisioned_audited_and_changes_only_new_acquisition_shape()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connection);
        await using var factory = new AdminPlanApiFactory(connection);
        var actorId = Guid.NewGuid();
        Guid planId;
        Guid grantId;
        Guid accountId;
        long revision;
        IReadOnlyList<AdminPlanEntitlementItem> entitlements;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var plan = await db.Plans.SingleAsync(value => value.Key == "standard");
            planId = plan.Id;
            revision = plan.Revision;
            entitlements = await db.PlanEntitlements.Where(value => value.PlanId == planId)
                .Select(value => new AdminPlanEntitlementItem(value.EntitlementKey, value.NumericValue, value.BooleanValue)).ToListAsync();
            grantId = Guid.NewGuid();
            accountId = Guid.NewGuid();
            db.AccountPlanGrants.Add(AccountPlanGrant.Create(grantId, accountId, planId, GrantSource.IndividualPurchase, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminPlanService>();
            var changed = entitlements.Select(value => value.Key == EntitlementCatalog.MaxImages
                ? value with { NumericValue = 2 }
                : value).ToArray();
            var result = await service.UpdateAsync(actorId, planId,
                new(revision, "  Standard Updated  ", " details ", 899.9900m, PlanBillingKind.Monthly, changed), CancellationToken.None);
            Assert.Equal(AdminPlanUpdateOutcome.Succeeded, result.Outcome);
            Assert.Equal("Standard Updated", result.Plan!.DisplayName);
            Assert.Equal(PlanBillingKind.Monthly, result.Plan.BillingKind);
            Assert.Equal(899.9900m, result.Plan.PriceAmount);
            Assert.Equal(revision + 1, result.Plan.Revision);

            var invalid = await service.UpdateAsync(actorId, planId,
                new(result.Plan.Revision, "Invalid", null, 899.99999m, PlanBillingKind.Monthly,
                    changed.Select(value => value.Key == EntitlementCatalog.MaxImages ? value with { NumericValue = 251 } : value).ToArray()),
                CancellationToken.None);
            Assert.Equal(AdminPlanUpdateOutcome.InvalidRequest, invalid.Outcome);

            var zeroMonthly = await service.UpdateAsync(actorId, planId,
                new(result.Plan.Revision, "Invalid zero price", null, 0m, PlanBillingKind.Monthly, changed), CancellationToken.None);
            Assert.Equal(AdminPlanUpdateOutcome.InvalidRequest, zeroMonthly.Outcome);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            var grant = await db.AccountPlanGrants.SingleAsync();
            var plan = await db.Plans.SingleAsync(value => value.Id == planId);
            Assert.Equal(PlanBillingKind.OneTime, grant.BillingKindAtGrant);
            Assert.Equal(PlanBillingKind.Monthly, plan.BillingKind);
            Assert.Equal(1, await db.AdminAuditRecords.CountAsync(record => record.EventType == "PlanSettingsUpdated" && record.SubjectId == planId));
            var grantReader = scope.ServiceProvider.GetRequiredService<IEntitlementGrantReader>();
            var snapshot = await grantReader.FindOwnedGrantAsync(accountId, grantId, CancellationToken.None);
            Assert.NotNull(snapshot);
            Assert.Equal(PlanBillingKind.OneTime, snapshot.PlanBillingKind);
        }

        async Task<AdminPlanUpdateOutcome> ConcurrentUpdateAsync(string name)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAdminPlanService>();
            return (await service.UpdateAsync(actorId, planId,
                new(revision + 1, name, "details", 900m, PlanBillingKind.Monthly, entitlements), CancellationToken.None)).Outcome;
        }

        var concurrent = await Task.WhenAll(ConcurrentUpdateAsync("Concurrent A"), ConcurrentUpdateAsync("Concurrent B"));
        Assert.Equal(1, concurrent.Count(value => value == AdminPlanUpdateOutcome.Succeeded));
        Assert.Equal(1, concurrent.Count(value => value == AdminPlanUpdateOutcome.Conflict));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(2, await db.AdminAuditRecords.CountAsync(record => record.EventType == "PlanSettingsUpdated" && record.SubjectId == planId));
        }
    }

    [Fact]
    public async Task Admin_global_settings_are_typed_revisioned_audited_and_retention_readers_observe_updates()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connection);
        await using var factory = new AdminPlanApiFactory(connection);
        var actorId = Guid.NewGuid();
        AdminSystemSettingItem invitation;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminSystemSettingsService>();
            var items = (await service.ListAsync(CancellationToken.None)).Items;
            Assert.Collection(items,
                item => Assert.Equal("abandonedMemoryRetentionDays", item.Key),
                item => Assert.Equal("deletedInvitationRetentionDays", item.Key));
            var list = items.ToDictionary(item => item.Key);
            Assert.Equal(30, list["abandonedMemoryRetentionDays"].Value);
            Assert.Equal(3, list["deletedInvitationRetentionDays"].Value);
            Assert.All(items, item =>
            {
                Assert.Equal(0, item.Minimum);
                Assert.Equal(365, item.Maximum);
                Assert.Equal(0, item.Revision);
            });
            invitation = list["deletedInvitationRetentionDays"];
        }

        async Task<AdminSystemSettingUpdateOutcome> UpdateInvitationAsync(int value, long revision)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAdminSystemSettingsService>();
            return (await service.UpdateAsync(actorId, invitation.Key,
                new(revision, value), CancellationToken.None)).Outcome;
        }

        var race = await Task.WhenAll(
            UpdateInvitationAsync(10, invitation.Revision),
            UpdateInvitationAsync(11, invitation.Revision));
        Assert.Equal(1, race.Count(value => value == AdminSystemSettingUpdateOutcome.Succeeded));
        Assert.Equal(1, race.Count(value => value == AdminSystemSettingUpdateOutcome.Conflict));
        Assert.Equal(AdminSystemSettingUpdateOutcome.InvalidRequest,
            await UpdateInvitationAsync(366, invitation.Revision + 1));
        Assert.Equal(AdminSystemSettingUpdateOutcome.InvalidRequest,
            await UpdateInvitationAsync(-1, invitation.Revision + 1));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminSystemSettingsService>();
            var current = (await service.ListAsync(CancellationToken.None)).Items
                .Single(item => item.Key == invitation.Key);
            Assert.Equal(invitation.Revision + 1, current.Revision);
            var liveInvitationReader = scope.ServiceProvider.GetRequiredService<IInvitationRetentionSettingsReader>();
            Assert.Equal(current.Value, await liveInvitationReader.ReadDaysAsync(CancellationToken.None));

            var abandonedMemory = (await service.ListAsync(CancellationToken.None)).Items
                .Single(item => item.Key == "abandonedMemoryRetentionDays");
            var updateMemory = await service.UpdateAsync(actorId, abandonedMemory.Key,
                new(abandonedMemory.Revision, 0), CancellationToken.None);
            Assert.Equal(AdminSystemSettingUpdateOutcome.Succeeded, updateMemory.Outcome);
            var noOp = await service.UpdateAsync(actorId, abandonedMemory.Key,
                new(updateMemory.Setting!.Revision, 0), CancellationToken.None);
            Assert.Equal(AdminSystemSettingUpdateOutcome.Succeeded, noOp.Outcome);
            Assert.Equal(updateMemory.Setting.Revision, noOp.Setting!.Revision);
            var liveReader = scope.ServiceProvider.GetRequiredService<IAbandonedMemoryRetentionSettingsReader>();
            Assert.Equal(0, await liveReader.ReadDaysAsync(CancellationToken.None));

            var db = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
            Assert.Equal(2, await db.AdminAuditRecords.CountAsync(record => record.EventType == "GlobalSettingUpdated"));
        }
    }

    private static async Task RunMigratorAsync(string connection)
    {
        var root = FindRepositoryRoot();
        var assembly = Path.Combine(root, "tools", "Davetiye.DatabaseMigrator", "bin", TestBuildConfiguration.Name, "net10.0", "Davetiye.DatabaseMigrator.dll");
        var info = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add(assembly);
        info.Environment["Database__ConnectionString"] = connection;
        info.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migration failed: {await output}{Environment.NewLine}{await error}");
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException();
    }
}
