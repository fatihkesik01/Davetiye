namespace Davetiye.Application.Modules.Memories.Contracts;

public interface IPublicMemoriesService
{
    Task<PublicMemoriesConfigurationResult> GetConfigurationAsync(string publicCode, CancellationToken cancellationToken);
    Task<PublicMemoriesListResult> ListAsync(string publicCode, int page, int pageSize, CancellationToken cancellationToken);
    Task<PublicMemoryDeliveryResult> CreateMediaDeliveryAsync(string publicCode, Guid memoryId, Guid assetId, CancellationToken cancellationToken);
    Task<PublicMemorySubmitResult> SubmitAsync(string publicCode, SubmitPublicMemoryRequest request, CancellationToken cancellationToken);
}

public sealed record SubmitPublicMemoryRequest(string? DisplayName, string? Text, string? Emoji);

/// <summary>Guest-form projection: only whether submissions are accepted and the input limits.</summary>
public sealed record PublicMemoriesConfiguration(string Status, PublicMemoryInputLimits Limits, PublicMemoriesGuestUploadLimits UploadLimits);
public sealed record PublicMemoryInputLimits(int MaxDisplayNameCharacters, int MaxTextCharacters, int MaxEmojiCharacters);
public sealed record PublicMemoriesGuestUploadLimits(bool Enabled, int MaxMediaItems, long MaxImages, long MaxVideos, long MaxImageSizeMb,
    long MaxVideoSizeMb, long MaxVideoDurationSeconds);

public sealed record PublicMemoryMediaItem(Guid AssetId, string Kind);
public sealed record PublicMemoryItem(Guid Id, string? DisplayName, string? Text, string? Emoji, DateTimeOffset CreatedAt,
    IReadOnlyList<PublicMemoryMediaItem> Media);
public sealed record PublicMemoriesPage(int Page, int PageSize, int TotalCount, IReadOnlyList<PublicMemoryItem> Items);
public sealed record PublicMemorySubmissionResponse(Guid MemoryId, DateTimeOffset CreatedAt);

public enum PublicMemoryOutcome { Available, NotFound, QuotaReached, Invalid, RateLimited, Unavailable }
public sealed record PublicMemoryDeliveryResult(PublicMemoryOutcome Outcome, string? MediaKind = null, Uri? Url = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>Per-invitation anonymous submission limiter, keyed by internal invitation id and only consulted after the gate passes.</summary>
public interface IPublicMemorySubmissionLimiter
{
    bool TryAcquire(Guid invitationId);
}
public sealed record PublicMemoriesConfigurationResult(PublicMemoryOutcome Outcome, PublicMemoriesConfiguration? Configuration = null);
public sealed record PublicMemoriesListResult(PublicMemoryOutcome Outcome, PublicMemoriesPage? Page = null);
public sealed record PublicMemorySubmitResult(PublicMemoryOutcome Outcome, PublicMemorySubmissionResponse? Memory = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
