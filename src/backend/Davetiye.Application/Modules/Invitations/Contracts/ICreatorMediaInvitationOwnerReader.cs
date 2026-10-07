namespace Davetiye.Application.Modules.Invitations.Contracts;

public interface ICreatorMediaInvitationOwnerReader
{
    Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<string?> GetOwnedTemplateKeyAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}
