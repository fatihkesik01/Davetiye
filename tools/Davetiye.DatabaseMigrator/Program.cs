using Davetiye.Infrastructure;
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
