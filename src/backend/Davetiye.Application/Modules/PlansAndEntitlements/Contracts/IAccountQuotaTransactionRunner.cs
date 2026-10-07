namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>
/// Serializes publication admission for one account and executes the callback in the same database
/// transaction. M3 must resolve the effective entitlement, read all account-level overlaps and
/// insert the accepted PublicationWindow inside this callback; a resolution/count made before the
/// lock is not authoritative. Any future cancel/revoke/release operation that frees account quota
/// must use this same runner so it cannot race a new admission.
/// </summary>
public interface IAccountQuotaTransactionRunner
{
    Task<TResult> ExecuteAsync<TResult>(
        Guid accountId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs an account-serialized operation in an outermost transaction and returns only after that
/// transaction commits. Callers that perform external side effects after this method may rely on
/// the database state being durable; an ambient transaction is rejected.
/// </summary>
public interface IOutermostAccountQuotaTransactionRunner
{
    Task<TResult> ExecuteAndCommitAsync<TResult>(
        Guid accountId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
