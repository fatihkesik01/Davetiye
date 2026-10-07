using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>Owns Organization renewal cancellation state and its durable provider intent.</summary>
public sealed class OrganizationSubscriptionAccountDeletionHandler(
    DavetiyeDbContext db,
    IOutboxWorkStore outbox) : IOrganizationSubscriptionAccountDeletionCommand
{
    private const string MessageType = "payments.organization-renewal-cancellation";

    public async Task QueueRenewalCancellationAsync(Guid accountId, DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || requestedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A valid account and UTC timestamp are required.");

        var subscriptions = await db.OrganizationSubscriptions
            .Where(subscription => subscription.AccountId == accountId &&
                subscription.RenewalCancellationCompletedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            if (!subscription.RequestRenewalCancellationImmediately(requestedAtUtc)) continue;
            var messageId = StableMessageId(subscription.Id);
            var payload = JsonSerializer.Serialize(new RenewalCancellationIntent(
                accountId, subscription.Id, subscription.ProviderName, subscription.ProviderSubscriptionId));
            await outbox.AppendAsync(new OutboxWorkAppend(messageId, MessageType, payload,
                requestedAtUtc, accountId), cancellationToken);
        }
    }

    private static Guid StableMessageId(Guid subscriptionId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"davetiye:organization-renewal-cancellation:{subscriptionId:N}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    private sealed record RenewalCancellationIntent(
        Guid AccountId,
        Guid SubscriptionId,
        string ProviderName,
        string ProviderSubscriptionId);
}
