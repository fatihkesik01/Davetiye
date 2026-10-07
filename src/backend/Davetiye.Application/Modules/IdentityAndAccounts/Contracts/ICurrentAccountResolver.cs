namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Narrow Identity &amp; Accounts boundary used by Creator-owned modules. The caller supplies only
/// the authenticated Identity user id taken from the server principal; AccountId is never accepted
/// from an API request.
/// </summary>
public interface ICurrentAccountResolver
{
    Task<Guid?> ResolveAccountIdAsync(Guid identityUserId, CancellationToken cancellationToken);
}
