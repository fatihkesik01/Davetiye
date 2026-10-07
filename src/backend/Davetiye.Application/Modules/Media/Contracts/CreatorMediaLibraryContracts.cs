using Davetiye.Domain.Modules.Media;

namespace Davetiye.Application.Modules.Media.Contracts;

public sealed record CreatorMediaAssetView(Guid AssetId, string Kind, string State,
    long? ByteLength, int? DurationSeconds, string RequestedPresentationRole,
    IReadOnlyList<CreatorMediaPlacementView> Placements);
public sealed record CreatorMediaPlacementView(string Role, int SortOrder);
public sealed record CreatorMediaLibraryResult(string Outcome, IReadOnlyList<CreatorMediaAssetView>? Assets = null);
public sealed record CreatorMediaPlacementCommand(Guid AccountId, Guid InvitationId, Guid AssetId,
    MediaPresentationRole Role, int SortOrder);
public sealed record CreatorMediaDeleteCommand(Guid AccountId, Guid InvitationId, Guid AssetId);
public sealed record MediaDeliveryResult(string Outcome, string? MediaKind = null, Uri? Url = null, DateTimeOffset? ExpiresAt = null);

public interface IPrivateMediaDeliveryGateway
{
    Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateImageCapabilityAsync(Guid assetId, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateVideoSessionAsync(Guid assetId, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public interface IPublicMediaDeliveryService
{
    Task<MediaDeliveryResult> CreateAsync(string publicCode, Guid assetId, CancellationToken cancellationToken);
}

/// <summary>Memories-only delivery port; Media verifies Guest scope, invitation ownership and Ready state.</summary>
public interface IGuestMemoryMediaDeliveryService
{
    Task<MediaDeliveryResult> CreateAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken);
    Task<bool> IsReadyForInvitationAsync(Guid invitationId, Guid assetId, CancellationToken cancellationToken);
}

public interface ICreatorMediaLibraryService
{
    Task<CreatorMediaLibraryResult> ListAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<CreatorMediaLibraryResult> SetPlacementAsync(CreatorMediaPlacementCommand command, CancellationToken cancellationToken);
    Task<CreatorMediaLibraryResult> DeleteAsync(CreatorMediaDeleteCommand command, CancellationToken cancellationToken);
}
