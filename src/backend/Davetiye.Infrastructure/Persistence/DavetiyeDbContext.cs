using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Persistence;

/// <summary>
/// The single DbContext for the whole application (ADR-0001). It extends
/// <see cref="IdentityUserContext{TUser,TKey}"/> (not the full <c>IdentityDbContext</c>) because
/// this milestone's schema needs the ASP.NET Core Identity user/claims/logins/tokens tables for
/// email+password and Google external-login persistence (docs/PRODUCT.md §3), but no accepted
/// product/ADR document describes an ASP.NET Core "Roles" claims table as part of the MVP
/// authorization model — Creator vs. Super Admin is a distinct principal/bootstrap concern
/// (ADR-0002), not a Roles-table concern. A later milestone can add role support if Security
/// decides it is actually needed.
/// </summary>
public class DavetiyeDbContext(DbContextOptions options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<BanRecord> BanRecords => Set<BanRecord>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();

    public DbSet<AccountPlanGrant> AccountPlanGrants => Set<AccountPlanGrant>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(DavetiyeDbContext).Assembly,
            type => type.Namespace?.StartsWith(
                "Davetiye.Infrastructure.Modules.",
                StringComparison.Ordinal) == true);

        modelBuilder.ApplyDavetiyePersistenceConventions();
    }
}
