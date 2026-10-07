using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>Claims only the documented HPP provider inbox and delegates transactional processing.</summary>
public sealed class PaymentWebhookWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentWebhookProcessingOptions> options,
    IClock clock,
    ILogger<PaymentWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                claimed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Inbox content, payment identifiers, and provider responses are never logged.
                logger.LogError("Payment inbox polling failed ({FailureType}); polling will continue.",
                    exception.GetType().Name);
            }

            if (claimed == 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 1, 300)), stoppingToken);
        }
    }

    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var settings = options.Value;
        var inbox = services.GetRequiredService<IPaymentWebhookProcessingStore>();
        var processor = services.GetRequiredService<PaymentWebhookProcessor>();
        var messages = await inbox.ClaimAsync(
            PaymentWebhookProcessor.ProviderName,
            Math.Clamp(settings.BatchSize, 1, 100),
            TimeSpan.FromSeconds(Math.Clamp(settings.LeaseSeconds, 10, 1800)),
            clock.UtcNow.ToUniversalTime(),
            cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await processor.ProcessAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A failed DB write leaves the lease to expire; it must not kill the hosted worker.
                logger.LogError("Payment inbox item processing failed ({FailureType}); the lease will expire.",
                    exception.GetType().Name);
            }
        }

        return messages.Count;
    }
}
