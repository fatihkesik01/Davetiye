using Davetiye.Infrastructure;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Per docs/adr/0002 ("Production Admin bootstrap public HTTP endpoint değil, tek seferlik kontrollü
// operasyon/CLI'dır"): this is a separate, one-time console executable, never a route reachable over
// HTTP, mirroring tools/Davetiye.DatabaseMigrator's shape exactly. The actual logic lives in the
// testable Davetiye.Infrastructure.Modules.IdentityAndAccounts.AdminBootstrapRunner; this file only
// parses arguments and wires it up.
var recoveryArguments = args.Contains("--recover-lost-mfa", StringComparer.Ordinal)
    ? RecoveryArguments.Parse(args)
    : null;
var arguments = recoveryArguments is null ? BootstrapArguments.Parse(args) : null;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<AdminBootstrapRunner>();
builder.Services.AddScoped<AdminMfaLostFactorRecoveryRunner>();

using var host = builder.Build();
await host.StartAsync();

await using var scope = host.Services.CreateAsyncScope();
if (recoveryArguments is not null)
{
    var recoveryRunner = scope.ServiceProvider.GetRequiredService<AdminMfaLostFactorRecoveryRunner>();
    var recoveryOutcome = await recoveryRunner.RunAsync(
        recoveryArguments.TargetUserId, recoveryArguments.PlatformOperatorId,
        confirmed: true, outOfBandIdentityVerified: true, CancellationToken.None);

    Console.WriteLine(recoveryOutcome switch
    {
        AdminMfaLostFactorRecoveryOutcome.Recovered =>
            "Recovered the Super Admin MFA factor. Existing sessions are revoked; the user must enroll MFA again.",
        AdminMfaLostFactorRecoveryOutcome.RefusedInvalidOperator =>
            "Refused: operator ID must identify an existing Super Admin identity without a Creator Account.",
        AdminMfaLostFactorRecoveryOutcome.RefusedNotSuperAdmin =>
            "Refused: target Identity user does not hold the Super Admin claim.",
        AdminMfaLostFactorRecoveryOutcome.RefusedCreatorAccount =>
            "Refused: target Super Admin identity has a Creator Account.",
        _ => throw new InvalidOperationException($"Unknown outcome '{recoveryOutcome}'."),
    });
}
else
{
    var runner = scope.ServiceProvider.GetRequiredService<AdminBootstrapRunner>();
    var outcome = await runner.RunAsync(arguments!.Email, arguments.Password, CancellationToken.None);

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

internal sealed record RecoveryArguments(Guid TargetUserId, Guid PlatformOperatorId)
{
    public static RecoveryArguments Parse(string[] args)
    {
        string? target = null;
        string? operatorId = null;
        var confirmed = false;
        var identityVerified = false;
        var commandSeen = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--recover-lost-mfa": commandSeen = true; break;
                case "--target-user-id": target = RequireValue(args, ref index, "--target-user-id"); break;
                case "--operator-id": operatorId = RequireValue(args, ref index, "--operator-id"); break;
                case "--confirm": confirmed = true; break;
                case "--out-of-band-identity-verified": identityVerified = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (!commandSeen || !confirmed || !identityVerified)
            throw new ArgumentException(
                "Recovery requires --recover-lost-mfa, --confirm, and " +
                "--out-of-band-identity-verified after platform-owner verification.");
        if (!Guid.TryParse(target, out var targetId) || targetId == Guid.Empty)
            throw new ArgumentException("--target-user-id must be a non-empty Identity user GUID.");
        if (!Guid.TryParse(operatorId, out var platformOperatorId) || platformOperatorId == Guid.Empty)
            throw new ArgumentException("--operator-id must be the platform operator's non-empty audit GUID.");

        return new RecoveryArguments(targetId, platformOperatorId);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"{optionName} requires a value.");
        index++;
        return args[index];
    }
}
