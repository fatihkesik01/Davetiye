using Davetiye.Infrastructure;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Per docs/adr/0002 ("Production Admin bootstrap public HTTP endpoint değil, tek seferlik kontrollü
// operasyon/CLI'dır"): this is a separate, one-time console executable, never a route reachable over
// HTTP, mirroring tools/Davetiye.DatabaseMigrator's shape exactly. The actual logic lives in the
// testable Davetiye.Infrastructure.Modules.IdentityAndAccounts.AdminBootstrapRunner; this file only
// parses arguments and wires it up.
var arguments = BootstrapArguments.Parse(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<AdminBootstrapRunner>();

using var host = builder.Build();
await host.StartAsync();

await using var scope = host.Services.CreateAsyncScope();
var runner = scope.ServiceProvider.GetRequiredService<AdminBootstrapRunner>();
var outcome = await runner.RunAsync(arguments.Email, arguments.Password, CancellationToken.None);

Console.WriteLine(outcome switch
{
    AdminBootstrapOutcome.Created =>
        $"Created a new Identity user for '{arguments.Email}' and granted the Super Admin claim.",
    AdminBootstrapOutcome.Granted =>
        $"Granted the Super Admin claim to the existing account '{arguments.Email}'.",
    AdminBootstrapOutcome.AlreadySuperAdmin =>
        $"'{arguments.Email}' is already a Super Admin. Nothing to do.",
    AdminBootstrapOutcome.RefusedExistingCreatorAccount =>
        $"Refused: '{arguments.Email}' already has a Creator account. A Super Admin principal must " +
        "not carry a Creator account (docs/adr/0002). The Super Admin claim was NOT granted.",
    _ => throw new InvalidOperationException($"Unknown outcome '{outcome}'."),
});

if (outcome is AdminBootstrapOutcome.Created or AdminBootstrapOutcome.Granted)
{
    Console.WriteLine(
        "Next: log in and call POST /api/v1/admin/mfa/enroll then /verify to complete MFA setup " +
        "before any Admin-only endpoint that requires the \"MfaComplete\" policy becomes usable.");
}

if (outcome == AdminBootstrapOutcome.RefusedExistingCreatorAccount)
{
    Environment.ExitCode = 1;
}

await host.StopAsync();

/// <summary>
/// Deliberately minimal manual argument parsing (no CLI-parsing package), mirroring
/// tools/Davetiye.DatabaseMigrator's own <c>ReadTargetMigration</c> style. <c>--confirm</c> is
/// required so granting platform-wide Super Admin access can never happen as an accidental side
/// effect of an otherwise-automated script.
/// </summary>
internal sealed record BootstrapArguments(string Email, string? Password)
{
    public static BootstrapArguments Parse(string[] args)
    {
        string? email = null;
        string? password = null;
        var confirmed = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--email":
                    email = RequireValue(args, ref index, "--email");
                    break;
                case "--password":
                    password = RequireValue(args, ref index, "--password");
                    break;
                case "--confirm":
                    confirmed = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "--email is required (e.g. --email admin@example.com --confirm).");
        }

        if (!confirmed)
        {
            throw new ArgumentException(
                "This operation grants platform-wide Super Admin access and must be invoked " +
                "deliberately. Re-run with --confirm to proceed.");
        }

        return new BootstrapArguments(email, password);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
}
