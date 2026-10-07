using Davetiye.Application.Modules.Invitations.Contracts;

namespace Davetiye.Application.Modules.Media.Contracts;

public interface IMediaPublicationSnapshotReader
{
    Task<IReadOnlyList<PublicSnapshotMediaPlacement>> ListReadyPlacementsAsync(Guid invitationId, CancellationToken cancellationToken);
}
