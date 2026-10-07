namespace Davetiye.Application.Modules.Memories.Contracts;

public interface ICreatorMemoryConfigurationService
{
    Task<CreatorMemoryConfigurationResult> GetAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<CreatorMemoryConfigurationResult> UpdateAsync(Guid accountId, Guid invitationId,
        UpdateMemoryConfigurationRequest request, CancellationToken cancellationToken);
}

/// <summary>Visibility is "CreatorOnly" or "Public".</summary>
public sealed record UpdateMemoryConfigurationRequest(long ExpectedRevision, bool IsEnabled, string? Visibility);

public sealed record CreatorMemoryConfiguration(Guid InvitationId, bool IsEnabled, string Visibility, long Revision,
    string EffectiveState, CreatorMemoryInputLimits InputLimits);
public sealed record CreatorMemoryInputLimits(int MaxDisplayNameCharacters, int MaxTextCharacters, int MaxEmojiCharacters,
    int MaxMemoriesPerInvitation);

public enum CreatorMemoryConfigurationOutcome { Succeeded, NotFound, Invalid, Conflict }
public sealed record CreatorMemoryConfigurationResult(CreatorMemoryConfigurationOutcome Outcome,
    CreatorMemoryConfiguration? Configuration = null, IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentRevision = null);

public enum CreatorMemoriesRateLimitBucket { Read, Write }

/// <summary>Account-keyed Creator Memories limit, applied after the authenticated identity resolves to its account.</summary>
public interface ICreatorMemoriesRateLimiter
{
    bool TryAcquire(Guid accountId, CreatorMemoriesRateLimitBucket bucket);
}
