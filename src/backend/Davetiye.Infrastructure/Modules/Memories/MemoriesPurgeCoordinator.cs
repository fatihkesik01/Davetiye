using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>Deletes the Memories-owned graph within the invitation's existing permanent-purge transaction.</summary>
public sealed class MemoriesPurgeCoordinator(DavetiyeDbContext dbContext) : IMemoriesPurgeCoordinator
{
    public async Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty) throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Memories graph deletion must run inside the permanent invitation-purge transaction.");

        // Memories own media links and upload capabilities (cascade). Media assets are removed
        // afterwards by the Media purge coordinator, which records provider deletion first.
        await dbContext.Memories
            .Where(memory => memory.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MemoryConfigurations
            .Where(configuration => configuration.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
