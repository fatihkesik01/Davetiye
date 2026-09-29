namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// MVP account kinds per docs/PRODUCT.md §3. Both are single-owner; Organization
/// membership/team roles are explicitly out of MVP scope and are not modeled here.
/// </summary>
public enum AccountType
{
    Individual,
    Organization
}
