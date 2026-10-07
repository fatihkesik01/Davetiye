using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class OrganizationSubscriptionLifecycleJobOptions
{
    public const string SectionName = "OrganizationSubscriptionLifecycleJobs";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
    public int RetryDelaySeconds { get; set; } = 300;
}

public static class OrganizationSubscriptionLifecycleWorkerRegistration
{
    public static IServiceCollection AddOrganizationSubscriptionLifecycleJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OrganizationSubscriptionLifecycleJobOptions>()
            .Bind(configuration.GetSection(OrganizationSubscriptionLifecycleJobOptions.SectionName))
            .Validate(options => options.IntervalSeconds is >= 1 and <= 3600 &&
                                 options.BatchSize is >= 1 and <= 1000 &&
                                 options.RetryDelaySeconds > options.IntervalSeconds &&
                                 options.RetryDelaySeconds <= 7200,
                "Organization subscription lifecycle cadence, batch size, and retry delay are outside technical bounds.")
            .ValidateOnStart();
        services.AddHostedService<OrganizationSubscriptionLifecycleWorker>();
        return services;
    }
}

public sealed class OrganizationSubscriptionLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrganizationSubscriptionLifecycleJobOptions> options,
    IClock clock,
    ILogger<OrganizationSubscriptionLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionLifecycleJobs>()
                        .EnqueueDueAccessExpiryRemindersAsync(
                            clock.UtcNow.ToUniversalTime(), settings.BatchSize,
                            TimeSpan.FromSeconds(settings.RetryDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning("Organization subscription maintenance batch failed ({FailureType}).",
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
