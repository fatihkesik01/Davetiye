using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

/// <summary>Deletes Gift Registry-owned rows within the invitation permanent-purge transaction.</summary>
public sealed class GiftRegistryPurgeCoordinator(DavetiyeDbContext dbContext) : IGiftRegistryPurgeCoordinator
{
    public async Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty) throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Gift Registry graph deletion must run inside the permanent invitation-purge transaction.");

        // Reservations contain guest identity/contact and have restrictive references to both
        // sessions and items, so remove them before those parent rows.
        await dbContext.GiftReservations
            .Where(reservation => reservation.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.GuestGiftSessions
            .Where(session => session.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.GiftItems
            .Where(item => item.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
