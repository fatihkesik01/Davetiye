using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// Server-side classification for feature-less web shells. Unexpected mixed Creator/Admin state
/// fails closed instead of receiving either surface.
/// </summary>
public sealed class AuthSessionAccessService(DavetiyeDbContext dbContext) : IAuthSessionAccessService
{
    public async Task<SessionAccessSnapshot> GetAccessAsync(
        Guid identityUserId,
        bool hasSuperAdminClaim,
        bool hasMfaClaim,
        CancellationToken cancellationToken)
    {
        var hasAccount = await dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(account => account.IdentityUserId == identityUserId && account.DeletionStartedAtUtc == null, cancellationToken);

        if (hasSuperAdminClaim)
        {
            return new SessionAccessSnapshot(
                hasAccount
                    ? SessionAccess.None
                    : hasMfaClaim
                        ? SessionAccess.MfaCompleteSuperAdmin
                        : SessionAccess.MfaSetupRequiredSuperAdmin);
        }

        return new SessionAccessSnapshot(hasAccount ? SessionAccess.Creator : SessionAccess.None);
    }
}
