using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

/// <summary>
/// Serializes publication admission by account using a PostgreSQL transaction-scoped advisory
/// lock. M3 adds its entitlement resolution, account-wide overlap query and PublicationWindow
/// insert inside this same callback. Future quota-releasing cancellation/revocation does the same.
/// </summary>
public sealed class AccountQuotaTransactionRunner(DavetiyeDbContext dbContext)
    : IAccountQuotaTransactionRunner, IOutermostAccountQuotaTransactionRunner
{
    public Task<TResult> ExecuteAndCommitAsync<TResult>(
        Guid accountId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "This operation requires its own committed account transaction and cannot run inside an ambient transaction.");
        }

        return ExecuteAsync(accountId, operation, cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid accountId,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        }

        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            // Publication returns typed denials after catching its abort exception. Isolate a
            // nested operation so that catching it cannot let an outer transaction commit an
            // earlier grant reservation/consumption without the rejected snapshot and window.
            var existingTransaction = dbContext.Database.CurrentTransaction;
            var savepointName = $"quota_{Guid.NewGuid():N}";
            await existingTransaction.CreateSavepointAsync(savepointName, cancellationToken);
            try
            {
                await AcquireLockAsync(accountId, cancellationToken);
                var result = await operation(cancellationToken);
                await existingTransaction.ReleaseSavepointAsync(savepointName, cancellationToken);
                return result;
            }
            catch
            {
                await existingTransaction.RollbackToSavepointAsync(savepointName, CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireLockAsync(accountId, cancellationToken);

            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private Task AcquireLockAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var lockKey = BitConverter.ToInt64(accountId.ToByteArray(), 0);
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }
}
