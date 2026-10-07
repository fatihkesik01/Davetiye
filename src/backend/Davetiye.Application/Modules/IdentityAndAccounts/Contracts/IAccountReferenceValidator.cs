namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Authoritative, narrow cross-module check for bare Account ids. It must query the Identity &amp;
/// Accounts-owned data; callers must not infer existence from a grant or accept AccountId from an
/// untrusted request as authorization.
/// </summary>
public interface IAccountReferenceValidator
{
    Task<AccountReferenceStatus> GetStatusAsync(Guid accountId, CancellationToken cancellationToken);
}

public enum AccountReferenceStatus
{
    NotFound,
    Unverified,
    Verified,
    Banned,
    Deleting
}
