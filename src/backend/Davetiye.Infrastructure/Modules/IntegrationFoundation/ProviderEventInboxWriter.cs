using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.IntegrationFoundation;

public sealed class ProviderEventInboxWriter(IInboxMessageRepository repository) : IProviderEventInboxWriter
{
    private const string ProviderEventUniqueIndex = "ix_inbox_messages_provider_name_provider_event_id";

    public async Task<ProviderEventInboxWriteOutcome> AppendAsync(
        string providerName,
        string providerEventId,
        string minimizedPayload,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var message = InboxMessage.Create(Guid.NewGuid(), providerName, providerEventId, minimizedPayload, receivedAt);
        try
        {
            await repository.AppendAsync(message, cancellationToken);
            return ProviderEventInboxWriteOutcome.Accepted;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return ProviderEventInboxWriteOutcome.Duplicate;
        }
    }

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProviderEventUniqueIndex
        };
}
