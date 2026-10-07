using Davetiye.Application.Modules.Administration.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Davetiye.Infrastructure.Modules.Administration;

/// <summary>Reduces registered health checks to two status labels; no check data or exception leaks.</summary>
public sealed class AdminOverviewHealthReader(HealthCheckService healthChecks) : IAdminOverviewHealthReader
{
    public async Task<AdminOverviewHealth> GetAsync(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains("live") || registration.Tags.Contains("ready"),
            cancellationToken);

        return new AdminOverviewHealth(
            StatusOf(report, "self"),
            StatusOf(report, "database"));
    }

    private static string StatusOf(HealthReport report, string name) =>
        report.Entries.TryGetValue(name, out var entry) ? entry.Status.ToString() : "Unknown";
}
