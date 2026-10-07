using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InvitationOwnershipValidator(DavetiyeDbContext dbContext)
    : IInvitationOwnershipValidator
{
    public Task<bool> IsOwnedByAccountAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken) =>
        dbContext.Invitations
            .AsNoTracking()
            .AnyAsync(
                invitation => invitation.Id == invitationId && invitation.AccountId == accountId,
                cancellationToken);
}
