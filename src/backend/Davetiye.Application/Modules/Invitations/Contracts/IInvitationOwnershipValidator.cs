namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>
/// Authoritative narrow ownership check for invitation references crossing into another module.
/// Returning one boolean deliberately does not distinguish a missing invitation from a foreign one.
/// </summary>
public interface IInvitationOwnershipValidator
{
    Task<bool> IsOwnedByAccountAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken);
}
