using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Invitation-owned public eligibility gate for Gift Registry.</summary>
public sealed class GiftGuestInvitationAccessReader(DavetiyeDbContext dbContext,
    IAccountReferenceValidator accounts, IPublicationGrantAccessValidator grants,
    ITemplateModuleSupportReader templates, IClock clock) : IGiftGuestInvitationAccessReader
{
    public Task<GiftGuestInvitationAccess?> ReadAsync(string publicCode, CancellationToken cancellationToken) =>
        ResolveAsync(publicCode, false, cancellationToken);

    public Task<GiftGuestInvitationAccess?> LockAndReadAsync(string publicCode, CancellationToken cancellationToken) =>
        ResolveAsync(publicCode, true, cancellationToken);

    private async Task<GiftGuestInvitationAccess?> ResolveAsync(string publicCode, bool lockInvitation, CancellationToken cancellationToken)
    {
        if (!PublicInvitationCode.IsValid(publicCode)) return null;
        if (lockInvitation)
        {
            if (dbContext.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Gift invitation locking requires an open transaction.");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM invitations WHERE public_code = {publicCode} AND deleted_at IS NULL FOR UPDATE", cancellationToken);
        }
        var query = lockInvitation ? dbContext.Invitations.AsQueryable() : dbContext.Invitations.AsNoTracking();
        var invitation = await query.SingleOrDefaultAsync(value => value.PublicCode == publicCode && value.DeletedAt == null, cancellationToken);
        if (invitation is null || string.IsNullOrWhiteSpace(invitation.TemplateKey)) return null;
        var window = await dbContext.PublicationWindows.AsNoTracking().SingleOrDefaultAsync(value =>
            value.InvitationId == invitation.Id && value.IsCurrent, cancellationToken);
        if (window is null) return null;
        var now = clock.UtcNow.ToUniversalTime();
        if (InvitationEffectiveStateEvaluator.Evaluate(invitation.State, window, now) != InvitationStoredState.Active ||
            await accounts.GetStatusAsync(invitation.AccountId, cancellationToken) != AccountReferenceStatus.Verified)
            return null;
        var grant = await grants.ReadAsync(invitation.AccountId, window.GrantId, cancellationToken);
        if (!PublicationGrantAccessPolicy.IsAllowed(grant, invitation.AccountId, invitation.Id, window.GrantId, window.StartsAt, now) ||
            !await templates.SupportsModuleAsync(invitation.TemplateKey, "giftRegistry", cancellationToken)) return null;
        return new(invitation.Id, invitation.AccountId, window.GrantId, invitation.TemplateKey, window.StartsAt, window.EndsAt);
    }
}
