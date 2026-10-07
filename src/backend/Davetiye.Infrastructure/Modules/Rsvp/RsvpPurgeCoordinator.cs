using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Rsvp;

/// <summary>Deletes the RSVP-owned graph within the invitation's existing permanent-purge transaction.</summary>
public sealed class RsvpPurgeCoordinator(DavetiyeDbContext dbContext) : IRsvpPurgeCoordinator
{
    public async Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        if (invitationId == Guid.Empty) throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("RSVP graph deletion must run inside the permanent invitation-purge transaction.");

        // Submissions own answers, selections, and manage capabilities. Remove them first so the
        // question/option foreign keys can remain restrictive and protect historical snapshots.
        await dbContext.RsvpSubmissions
            .Where(submission => submission.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RsvpConfigurations
            .Where(configuration => configuration.InvitationId == invitationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
