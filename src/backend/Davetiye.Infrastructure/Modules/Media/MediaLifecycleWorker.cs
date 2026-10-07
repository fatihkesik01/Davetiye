using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class MediaLifecycleJobOptions
{
    public const string SectionName = "MediaLifecycleJobs";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 100;
    public int ClaimLeaseSeconds { get; set; } = 300;
    public int MaximumAttempts { get; set; } = 12;
    public int InitialRetryDelaySeconds { get; set; } = 5;
    public int MaximumRetryDelaySeconds { get; set; } = 3600;
}

public static class MediaLifecycleWorkerRegistration
{
    public static IServiceCollection AddMediaLifecycleJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MediaLifecycleJobOptions>().Bind(configuration.GetSection(MediaLifecycleJobOptions.SectionName))
            .Validate(settings => settings.IntervalSeconds is >= 1 and <= 3600 &&
                settings.BatchSize is >= 1 and <= 1000 && settings.ClaimLeaseSeconds is >= 10 and <= 3600 &&
                settings.MaximumAttempts is >= 1 and <= 100 && settings.InitialRetryDelaySeconds is >= 1 and <= 3600 &&
                settings.MaximumRetryDelaySeconds >= settings.InitialRetryDelaySeconds && settings.MaximumRetryDelaySeconds <= 86400,
                "Media lifecycle cadence, retry or batch values are outside technical bounds.")
            .ValidateOnStart();
        services.AddHostedService<MediaLifecycleWorker>();
        return services;
    }
}

public sealed class MediaLifecycleWorker(IServiceScopeFactory scopes, IOptions<MediaLifecycleJobOptions> options,
    ILogger<MediaLifecycleWorker> logger) : BackgroundService
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
                    await scope.ServiceProvider.GetRequiredService<IMediaLifecycleJobs>()
                        .RunBatchAsync(settings.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogWarning(exception, "Media lifecycle maintenance batch failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
