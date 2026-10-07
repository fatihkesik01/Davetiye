using Davetiye.Infrastructure;
using Davetiye.Infrastructure.Modules.Administration;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Memories;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var targetMigration = ReadTargetMigration(args);
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<DatabaseMigrationRunner>();

using var host = builder.Build();
await host.StartAsync();

await using var scope = host.Services.CreateAsyncScope();
var runner = scope.ServiceProvider.GetRequiredService<DatabaseMigrationRunner>();
await runner.MigrateAsync(targetMigration);

// Reconcile code-owned starter catalogs only after a forward migration to the current schema.
// Explicit --target runs are used for upgrade/downgrade verification and may intentionally target a
// schema that predates template_definitions, so they must remain migration-only.
if (targetMigration is null)
{
    var planCatalogInitializer = scope.ServiceProvider.GetRequiredService<PlanCatalogInitializer>();
    await planCatalogInitializer.InitializeAsync(CancellationToken.None);

    var retentionInitializer = scope.ServiceProvider.GetRequiredService<InvitationRetentionSettingsInitializer>();
    await retentionInitializer.InitializeAsync(CancellationToken.None);

    var abandonedMemoryRetentionInitializer = scope.ServiceProvider.GetRequiredService<AbandonedMemoryRetentionSettingsInitializer>();
    await abandonedMemoryRetentionInitializer.InitializeAsync(CancellationToken.None);

    var templateCatalogInitializer = scope.ServiceProvider.GetRequiredService<TemplateCatalogInitializer>();
    await templateCatalogInitializer.InitializeAsync(CancellationToken.None);
}

await host.StopAsync();

static string? ReadTargetMigration(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--target")
        {
            if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new ArgumentException("--target requires a migration name or 0.");
            }

            return arguments[index + 1];
        }
    }

    return null;
}
