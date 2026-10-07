using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Invitation-owned public eligibility gate for Memories; exposes only identifiers and the accepted window.</summary>
public sealed class MemoriesGuestInvitationAccessReader(
    DavetiyeDbContext dbContext,
    IAccountReferenceValidator accounts,
    IPublicationGrantAccessValidator grants,
    IClock clock) : IMemoriesGuestInvitationAccessReader
{
    public Task<InvitationMemoriesGuestAccess?> ReadAsync(string publicCode, CancellationToken cancellationToken) =>
        ResolveAsync(publicCode, lockInvitation: false, cancellationToken);

    public Task<InvitationMemoriesGuestAccess?> LockAndReadAsync(string publicCode, CancellationToken cancellationToken) =>
        ResolveAsync(publicCode, lockInvitation: true, cancellationToken);

    private async Task<InvitationMemoriesGuestAccess?> ResolveAsync(string publicCode, bool lockInvitation,
        CancellationToken cancellationToken)
    {
        if (!PublicInvitationCode.IsValid(publicCode)) return null;
        if (lockInvitation)
        {
            if (dbContext.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Memories invitation locking requires an open transaction.");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM invitations WHERE public_code = {publicCode} AND deleted_at IS NULL FOR UPDATE",
                cancellationToken);
        }

        var query = lockInvitation ? dbContext.Invitations.AsQueryable() : dbContext.Invitations.AsNoTracking();
        var invitation = await query.SingleOrDefaultAsync(item => item.PublicCode == publicCode && item.DeletedAt == null,
            cancellationToken);
        if (invitation is null) return null;
        var window = await dbContext.PublicationWindows.AsNoTracking().SingleOrDefaultAsync(item =>
            item.InvitationId == invitation.Id && item.IsCurrent, cancellationToken);
        if (window is null) return null;
        var now = clock.UtcNow.ToUniversalTime();
        if (InvitationEffectiveStateEvaluator.Evaluate(invitation.State, window, now) != InvitationStoredState.Active)
            return null;
        if (await accounts.GetStatusAsync(invitation.AccountId, cancellationToken) != AccountReferenceStatus.Verified)
            return null;
        var grant = await grants.ReadAsync(invitation.AccountId, window.GrantId, cancellationToken);
        if (!PublicationGrantAccessPolicy.IsAllowed(grant, invitation.AccountId, invitation.Id,
                window.GrantId, window.StartsAt, now)) return null;

        // No template module key exists for Memories (catalog modules list rsvp/gallery/giftRegistry only), so
        // no template-support gate is applied; only entitlement + Creator IsEnabled gate the module (P6-M2).
        return new(invitation.Id, invitation.AccountId, window.GrantId, window.StartsAt, window.EndsAt);
    }
}
