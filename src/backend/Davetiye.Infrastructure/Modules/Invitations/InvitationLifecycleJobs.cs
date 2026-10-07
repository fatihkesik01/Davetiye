using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InvitationLifecycleJobs(DavetiyeDbContext dbContext,
    IAccountQuotaTransactionRunner runner, IPublicationGrantLifecycleService grants,
    IPublicationGrantAccessValidator grantAccessValidator,
    IMediaPurgeCoordinator mediaPurge, IRsvpPurgeCoordinator rsvpPurge,
    IMemoriesPurgeCoordinator memoriesPurge, IGiftRegistryPurgeCoordinator giftRegistryPurge,
    IClock clock, ILogger<InvitationLifecycleJobs> logger) : IInvitationLifecycleJobs
{
    public async Task<InvitationLifecycleJobResult> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.UtcNow.ToUniversalTime();
        var expiredOrganizationGrantIds = await grantAccessValidator.FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
            now, batchSize, cancellationToken);
        var candidates = await dbContext.Invitations.IgnoreQueryFilters().AsNoTracking()
            .Where(invitation => invitation.DeletedAt != null && invitation.PurgeAfter <= now ||
                invitation.DeletedAt == null && dbContext.PublicationWindows.Any(window =>
                    window.InvitationId == invitation.Id && window.IsCurrent &&
                    (invitation.State == InvitationStoredState.Scheduled ||
                     invitation.State == InvitationStoredState.Active ||
                     invitation.State == InvitationStoredState.Paused) &&
                    (window.StartsAt <= now || window.EndsAt <= now ||
                     expiredOrganizationGrantIds.Contains(window.GrantId))))
            .OrderBy(invitation => invitation.Id).Select(invitation => new { invitation.Id, invitation.AccountId })
            .Take(batchSize).ToListAsync(cancellationToken);
        var activated = 0; var expired = 0; var purged = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var outcome = await runner.ExecuteAsync(candidate.AccountId, async token =>
                {
                    // Trusted maintenance bypasses the deletion overlay, never owner admission.
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM invitations WHERE id = {candidate.Id} FOR UPDATE", token);
                    var invitation = await dbContext.Invitations.IgnoreQueryFilters()
                        .SingleOrDefaultAsync(item => item.Id == candidate.Id, token);
                    if (invitation is null) return 0;
                    await dbContext.Entry(invitation).ReloadAsync(token);
                    var window = await dbContext.PublicationWindows.SingleOrDefaultAsync(
                        item => item.InvitationId == candidate.Id && item.IsCurrent, token);
                    if (window is not null) await dbContext.Entry(window).ReloadAsync(token);
                    var current = clock.UtcNow.ToUniversalTime();
                    if (invitation.DeletedAt is not null)
                    {
                        if (invitation.PurgeAfter is null || current < invitation.PurgeAfter) return 0;
                        if (window is not null && window.StartsAt <= current)
                            await grants.ConsumeStartedAsync(invitation.AccountId, invitation.Id,
                                window.GrantId, window.StartsAt, token);
                        // Memories rows go first; their media links point at assets the Media purge removes next.
                        await memoriesPurge.PurgeForInvitationAsync(invitation.Id, token);
                        // Gift Registry owns its session, item, reservation, and guest PII rows.
                        await giftRegistryPurge.PurgeForInvitationAsync(invitation.Id, token);
                        await mediaPurge.EnqueueAndRemoveInvitationAssetsAsync(invitation.Id, token);
                        await rsvpPurge.PurgeForInvitationAsync(invitation.Id, token);
                        dbContext.Invitations.Remove(invitation);
                        await dbContext.SaveChangesAsync(token);
                        return 3;
                    }

                    if (window is null ||
                        invitation.State is not (InvitationStoredState.Scheduled or InvitationStoredState.Active or InvitationStoredState.Paused))
                        return 0;

                    if (expiredOrganizationGrantIds.Contains(window.GrantId))
                    {
                        var grantSnapshot = await grantAccessValidator.ReadAsync(
                            invitation.AccountId, window.GrantId, token);
                        if (PublicationGrantAccessPolicy.IsExpiredOrganizationGrant(grantSnapshot,
                                invitation.AccountId, invitation.Id, window.GrantId, window.StartsAt, current))
                        {
                            invitation.ChangePublicationState(InvitationStoredState.Expired);
                            window.MarkHistorical();
                            await dbContext.SaveChangesAsync(token);
                            return 2;
                        }
                    }

                    if (window.StartsAt > current) return 0;
                    await grants.ConsumeStartedAsync(invitation.AccountId, invitation.Id, window.GrantId, window.StartsAt, token);
                    current = clock.UtcNow.ToUniversalTime();
                    // Stored-state reconciliation cannot grant public authority: the public gate
                    // independently validates assignment/revocation and the clock on every read.
                    if (window.EndsAt <= current)
                    {
                        invitation.ChangePublicationState(InvitationStoredState.Expired);
                        await dbContext.SaveChangesAsync(token);
                        return 2;
                    }
                    if (invitation.State == InvitationStoredState.Scheduled)
                    {
                        invitation.ChangePublicationState(InvitationStoredState.Active);
                        await dbContext.SaveChangesAsync(token);
                        return 1;
                    }
                    return 0;
                }, cancellationToken);
                if (outcome == 1) activated++;
                if (outcome == 2) expired++;
                if (outcome == 3) purged++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Invitation lifecycle maintenance failed for {InvitationId}.", candidate.Id);
            }
            finally { dbContext.ChangeTracker.Clear(); }
        }
        return new(candidates.Count, activated, expired, purged);
    }
}
