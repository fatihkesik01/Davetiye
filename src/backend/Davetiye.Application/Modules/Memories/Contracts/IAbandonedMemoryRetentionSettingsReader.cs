namespace Davetiye.Application.Modules.Memories.Contracts;

public interface IAbandonedMemoryRetentionSettingsReader
{
    Task<int?> ReadDaysAsync(CancellationToken cancellationToken);
}
