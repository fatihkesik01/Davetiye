namespace Davetiye.Application.Modules.Memories.Contracts;

/// <summary>
/// Anonymous guest upload surface (P6-M3). Every operation after the memory is created is authorized only by the
/// short-lived, single-memory upload capability (HttpOnly cookie, never a persistent guest token); every failure of
/// that authorization, the invitation gates, or ownership collapses to <see cref="PublicMemoryUploadOutcome.NotFound"/>.
/// </summary>
public interface IPublicMemoryUploadService
{
    /// <summary>Creates a PendingMedia memory and issues its upload capability.</summary>
    Task<PublicMemoryUploadSessionResult> CreateAsync(string publicCode, SubmitPublicMemoryRequest request,
        CancellationToken cancellationToken);

    Task<PublicMemoryUploadIntentResult> CreateIntentAsync(string publicCode, Guid memoryId, string? capabilityToken,
        CreatePublicMemoryMediaIntentRequest request, Guid idempotencyKey, CancellationToken cancellationToken);

    Task<PublicMemoryUploadFinalizeResult> FinalizeAsync(string publicCode, Guid memoryId, string? capabilityToken,
        CancellationToken cancellationToken);

    Task<PublicMemoryUploadStatusResult> GetStatusAsync(string publicCode, Guid memoryId, string? capabilityToken,
        CancellationToken cancellationToken);
}

public enum PublicMemoryUploadOutcome
{
    Ok,
    NotFound,
    Invalid,
    RateLimited,
    /// <summary>A memory/media cap, quota, or idempotency conflict (HTTP 409 with a machine-readable code).</summary>
    Conflict,
    /// <summary>The provider could not issue an upload capability; retrying with the same idempotency key is safe.</summary>
    UploadUnavailable
}

public sealed record PublicMemoryUploadLimits(int MaxMediaItems, long MaxImageBytes, long MaxVideoBytes, long MaxVideoDurationSeconds);

public sealed record PublicMemoryUploadSession(Guid MemoryId, DateTimeOffset CreatedAt, DateTimeOffset UploadExpiresAt,
    PublicMemoryUploadLimits Limits);

public sealed record PublicMemoryUploadSessionResult(PublicMemoryUploadOutcome Outcome, PublicMemoryUploadSession? Session = null,
    string? CapabilityToken = null, IReadOnlyDictionary<string, string[]>? Errors = null, string? Code = null);

/// <summary>Kind is "Image" or "Video"; DeclaredDurationSeconds is required for video.</summary>
public sealed record CreatePublicMemoryMediaIntentRequest(string? Kind, long DeclaredByteLength, long? DeclaredDurationSeconds);

public sealed record PublicMemoryUploadIntent(Guid AssetId, string Kind, DateTimeOffset UploadExpiresAt, bool Replayed,
    Uri IngressUri, DateTimeOffset CapabilityExpiresAt, IReadOnlyDictionary<string, string> IngressHeaders);

public sealed record PublicMemoryUploadIntentResult(PublicMemoryUploadOutcome Outcome, PublicMemoryUploadIntent? Intent = null,
    IReadOnlyDictionary<string, string[]>? Errors = null, string? Code = null);

/// <summary>Published: memory accepted; Processing: media still being verified (capability stays valid); Rejected: nothing publishable, memory discarded.</summary>
public enum PublicMemoryFinalizeState { Published, Processing, Rejected }

public sealed record PublicMemoryUploadFinalizeResult(PublicMemoryUploadOutcome Outcome, PublicMemoryFinalizeState? State = null,
    int AcceptedMediaCount = 0, int RejectedMediaCount = 0);

public sealed record PublicMemoryUploadMediaStatus(Guid AssetId, string Kind, string Status);

public sealed record PublicMemoryUploadStatus(string State, DateTimeOffset UploadExpiresAt,
    IReadOnlyList<PublicMemoryUploadMediaStatus> Media);

public sealed record PublicMemoryUploadStatusResult(PublicMemoryUploadOutcome Outcome, PublicMemoryUploadStatus? Status = null);

/// <summary>Per-invitation limiter for guest upload intents, keyed by internal invitation id and consulted only after the capability is valid.</summary>
public interface IPublicMemoryUploadIntentLimiter
{
    bool TryAcquire(Guid invitationId);
}

/// <summary>
/// Abandons PendingMedia memories whose upload capability expired and discards their Media assets through the
/// Media port (PD-16: bytes and quota are retained until permanent purge). Invoked by the media lifecycle job.
/// </summary>
public interface IMemoryUploadExpirySweeper
{
    Task<int> SweepAsync(int batchSize, CancellationToken cancellationToken);
}
