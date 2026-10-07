using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Owns invitation purge scheduling for verified account deletion.</summary>
public sealed class InvitationAccountDeletionHandler(DavetiyeDbContext db) : IInvitationAccountDeletionCommand
{
    public async Task SchedulePermanentPurgeAsync(Guid accountId, DateTimeOffset scheduledAtUtc,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || scheduledAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A valid account and UTC timestamp are required.");

        var invitations = await db.Invitations.IgnoreQueryFilters()
            .Where(invitation => invitation.AccountId == accountId)
            .ToListAsync(cancellationToken);
        foreach (var invitation in invitations)
            invitation.SchedulePermanentPurge(scheduledAtUtc);
    }
}
