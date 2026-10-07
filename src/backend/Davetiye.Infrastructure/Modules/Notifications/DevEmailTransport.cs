using Davetiye.Application.Modules.Notifications.Contracts;
using Microsoft.Extensions.Logging;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>Local transport that never contacts an external provider or logs recipients/content.</summary>
public sealed class DevEmailTransport(ILogger<DevEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(EmailDeliveryMessage message, Guid idempotencyId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Development email delivered to local sink. MessageId={MessageId}", idempotencyId);
        return Task.CompletedTask;
    }
}
