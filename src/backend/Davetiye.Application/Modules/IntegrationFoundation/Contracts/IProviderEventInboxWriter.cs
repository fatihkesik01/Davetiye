namespace Davetiye.Application.Modules.IntegrationFoundation.Contracts;

/// <summary>
/// Narrow provider-neutral write port for typed modules to durably queue a minimized event envelope
/// without depending on the Integration Foundation persistence entity.
/// </summary>
public interface IProviderEventInboxWriter
{
    Task<ProviderEventInboxWriteOutcome> AppendAsync(
        string providerName,
        string providerEventId,
        string minimizedPayload,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken);
}

public enum ProviderEventInboxWriteOutcome
{
    Accepted,
    Duplicate
}
