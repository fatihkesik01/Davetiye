using Davetiye.Application.Modules.Invitations.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InvitationLifecycleJobOptions
{
    public const string SectionName = "InvitationLifecycleJobs";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
}

public static class InvitationLifecycleWorkerRegistration
{
    // API-only registration: the migrator never starts maintenance before schema readiness.
    public static IServiceCollection AddInvitationLifecycleJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InvitationLifecycleJobOptions>().Bind(configuration.GetSection(InvitationLifecycleJobOptions.SectionName))
            .Validate(options => options.IntervalSeconds is >= 1 and <= 3600 && options.BatchSize is >= 1 and <= 1000,
                "Invitation lifecycle cadence and batch size are outside technical bounds.").ValidateOnStart();
        services.AddHostedService<InvitationLifecycleWorker>();
        return services;
    }
}

public sealed class InvitationLifecycleWorker(IServiceScopeFactory scopes,
    IOptions<InvitationLifecycleJobOptions> options, ILogger<InvitationLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IInvitationLifecycleJobs>()
                        .RunBatchAsync(settings.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogWarning(exception, "Invitation lifecycle maintenance batch failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
