namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>Minimal cross-module read used to suppress queued messages after owner deletion starts.</summary>
public interface IAccountDeletionStatusReader
{
    Task<bool> IsDeletingAsync(Guid accountId, CancellationToken cancellationToken);
}
