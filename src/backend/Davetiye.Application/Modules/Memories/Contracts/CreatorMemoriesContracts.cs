namespace Davetiye.Application.Modules.Memories.Contracts;

public interface ICreatorMemoriesService
{
    Task<CreatorMemoriesListResult> ListAsync(Guid accountId, Guid invitationId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<CreatorMemoryModerationResult> HideAsync(Guid accountId, Guid invitationId, Guid memoryId,
        CancellationToken cancellationToken);
    Task<CreatorMemoryModerationResult> DeleteAsync(Guid accountId, Guid invitationId, Guid memoryId,
        CancellationToken cancellationToken);
    Task<CreatorMemoryDeliveryResult> CreateMediaDeliveryAsync(Guid accountId, Guid invitationId, Guid memoryId,
        Guid assetId, CancellationToken cancellationToken);
}

public sealed record CreatorMemoriesPage(int Page, int PageSize, int TotalCount, IReadOnlyList<CreatorMemoryItem> Items);
public sealed record CreatorMemoryItem(Guid Id, string? DisplayName, string? Text, string? Emoji, string State,
    DateTimeOffset CreatedAt, IReadOnlyList<CreatorMemoryMediaItem> Media);
public sealed record CreatorMemoryMediaItem(Guid AssetId, string Kind, string Status);

public enum CreatorMemoriesOutcome { Succeeded, NotFound, Conflict, Unavailable }
public sealed record CreatorMemoriesListResult(CreatorMemoriesOutcome Outcome, CreatorMemoriesPage? Page = null);
public sealed record CreatorMemoryModerationResult(CreatorMemoriesOutcome Outcome);
public sealed record CreatorMemoryDeliveryResult(CreatorMemoriesOutcome Outcome, string? MediaKind = null, Uri? Url = null,
    DateTimeOffset? ExpiresAt = null);
public sealed record CreatorMemoryPreviewResponse(string MediaKind, Uri DeliveryUrl, DateTimeOffset ExpiresAt);
