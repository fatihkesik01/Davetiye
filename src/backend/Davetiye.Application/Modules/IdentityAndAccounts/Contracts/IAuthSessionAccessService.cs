namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Supplies only the access classification a browser route guard needs. It deliberately exposes no
/// account identifier, profile data or persisted claims.
/// </summary>
public interface IAuthSessionAccessService
{
    Task<SessionAccessSnapshot> GetAccessAsync(
        Guid identityUserId,
        bool hasSuperAdminClaim,
        bool hasMfaClaim,
        CancellationToken cancellationToken);
}

public enum SessionAccess
{
    None,
    Creator,
    MfaSetupRequiredSuperAdmin,
    MfaCompleteSuperAdmin,
}

public sealed record SessionAccessSnapshot(SessionAccess Access);
