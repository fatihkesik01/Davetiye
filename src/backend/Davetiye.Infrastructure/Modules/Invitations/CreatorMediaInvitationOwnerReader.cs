using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class CreatorMediaInvitationOwnerReader(DavetiyeDbContext db) : ICreatorMediaInvitationOwnerReader
{
    public Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
        db.Invitations.AsNoTracking().AnyAsync(item => item.Id == invitationId && item.AccountId == accountId && item.DeletedAt == null, cancellationToken);

    public Task<string?> GetOwnedTemplateKeyAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
        db.Invitations.AsNoTracking().Where(item => item.Id == invitationId && item.AccountId == accountId && item.DeletedAt == null)
            .Select(item => item.TemplateKey).SingleOrDefaultAsync(cancellationToken);
}
