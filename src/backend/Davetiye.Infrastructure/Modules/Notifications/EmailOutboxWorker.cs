using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Notifications;

public sealed class EmailOutboxOptions
{
    public const string SectionName = "EmailOutbox";
    public int BatchSize { get; init; } = 20;
    public int PollIntervalSeconds { get; init; } = 5;
    public int LeaseSeconds { get; init; } = 120;
    public int RetryBaseSeconds { get; init; } = 10;
    public int RetryMaximumSeconds { get; init; } = 3600;
    public int MaximumDeliveryAgeHours { get; init; } = 20;
}

/// <summary>Durably dispatches protected email messages and applies bounded retry backoff.</summary>
public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOutboxOptions> options,
    IClock clock,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    public const string MessageType = "notifications.email";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A database outage, stale lease, or failure persisting retry state must not stop
                // the hosted dispatcher. Do not include outbox content or recipient data.
                logger.LogError("Email outbox polling failed ({FailureType}); polling will continue.", exception.GetType().Name);
            }

            if (processed == 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 1, 300)), stoppingToken);
        }
    }

    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IOutboxWorkStore>();
        var settings = options.Value;
        var now = clock.UtcNow;
        var workItems = await store.ClaimAsync(MessageType, Math.Clamp(settings.BatchSize, 1, 100),
            TimeSpan.FromSeconds(Math.Clamp(settings.LeaseSeconds, 10, 1800)), now, cancellationToken);
        foreach (var work in workItems)
            await ProcessOneAsync(work, store, services, settings, cancellationToken);
        return workItems.Count;
    }

    private async Task ProcessOneAsync(ClaimedOutboxWork work, IOutboxWorkStore store,
        IServiceProvider services, EmailOutboxOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            // All supported production email producers attach their owning account. Older outbox
            // rows predate that linkage, so their recipient cannot be checked against deletion
            // state. Quarantine them terminally rather than risk sending to a deleted account.
            if (work.OwnerAccountId is null)
            {
                await store.FailAsync(work.Receipt, clock.UtcNow, null, cancellationToken);
                logger.LogWarning("Ownerless legacy email outbox message was quarantined. MessageId={MessageId}",
                    work.Receipt.MessageId);
                return;
            }

            if (EmailOutboxDeliveryPolicy.IsOverdue(work.CreatedAt, clock.UtcNow, settings.MaximumDeliveryAgeHours))
            {
                await store.FailAsync(work.Receipt, clock.UtcNow, null, cancellationToken);
                logger.LogWarning("Email outbox message exceeded its safe retry window and was discarded. MessageId={MessageId}", work.Receipt.MessageId);
                return;
            }

            var (envelope, expired) = services.GetRequiredService<EmailOutboxPayloadProtector>().Unprotect(work.Payload, clock.UtcNow);
            if (expired)
            {
                await store.FailAsync(work.Receipt, clock.UtcNow, null, cancellationToken);
                logger.LogInformation("Expired authentication email discarded. MessageId={MessageId}", work.Receipt.MessageId);
                return;
            }

            var rendered = services.GetRequiredService<EmailTemplateRenderer>()
                .Render(envelope.ToEmail, envelope.Kind, envelope.Data);
            if (work.OwnerAccountId is { } ownerAccountId)
            {
                var authorization = await services.GetRequiredService<IEmailOwnedDispatchGate>()
                    .AuthorizeAsync(work.Receipt, ownerAccountId, clock.UtcNow, cancellationToken);
                if (authorization == OwnedOutboxDispatchOutcome.Suppressed)
                {
                    logger.LogInformation("Account-owned email suppressed after deletion. MessageId={MessageId}", work.Receipt.MessageId);
                    return;
                }
                if (authorization == OwnedOutboxDispatchOutcome.LeaseLost)
                {
                    logger.LogDebug("Email claim expired before dispatch authorization. MessageId={MessageId}", work.Receipt.MessageId);
                    return;
                }
            }
            await services.GetRequiredService<IEmailTransport>().SendAsync(rendered, work.Receipt.MessageId, cancellationToken);
            await store.CompleteAsync(work.Receipt, clock.UtcNow, cancellationToken);
        }
        catch (EmailDeliveryException exception)
        {
            DateTimeOffset? retryAt = exception.IsTransient ? clock.UtcNow + RetryDelay(settings, work.AttemptCount) : null;
            await store.FailAsync(work.Receipt, clock.UtcNow, retryAt, cancellationToken);
            logger.LogWarning("Email delivery failed. MessageId={MessageId} Transient={Transient}", work.Receipt.MessageId, exception.IsTransient);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException)
        {
            await store.FailAsync(work.Receipt, clock.UtcNow, null, cancellationToken);
            logger.LogWarning("Invalid email outbox message discarded. MessageId={MessageId} FailureType={FailureType}",
                work.Receipt.MessageId, exception.GetType().Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await store.FailAsync(work.Receipt, clock.UtcNow, clock.UtcNow + RetryDelay(settings, work.AttemptCount), cancellationToken);
            logger.LogWarning("Email delivery will retry. MessageId={MessageId} FailureType={FailureType}",
                work.Receipt.MessageId, exception.GetType().Name);
        }
    }

    private static TimeSpan RetryDelay(EmailOutboxOptions options, int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount, 0, 16);
        var seconds = Math.Min((double)Math.Clamp(options.RetryBaseSeconds, 1, 3600) * Math.Pow(2, exponent),
            Math.Clamp(options.RetryMaximumSeconds, 1, 86_400));
        return TimeSpan.FromSeconds(seconds);
    }
}
