using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>Atomically records an account ban, minimized audit entry, and target security-stamp rotation.</summary>
public sealed class AdminBanService(
    DavetiyeDbContext db,
    UserManager<ApplicationUser> userManager,
    IAdminAuditWriter audit,
    IClock clock) : IAdminBanService
{
    public const string AuditEventType = "AccountBanned";
    public const string UnbanAuditEventType = "AccountUnbanned";
    public const int MaxReasonLength = 2000;

    public async Task<AdminBanResult> BanAsync(
        Guid actorIdentityUserId,
        Guid accountId,
        AdminBanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim();
        var internalNote = string.IsNullOrWhiteSpace(request.InternalNote) ? null : request.InternalNote.Trim();
        if (actorIdentityUserId == Guid.Empty || accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength ||
            internalNote?.Length > BanRecord.MaxInternalNoteLength)
        {
            return new(AdminBanOutcome.InvalidRequest);
        }

        var now = clock.UtcNow.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize concurrent ban attempts for the same target account. The partial unique index
        // remains a second line of defense for any future writer that does not take this lock.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM accounts WHERE id = {accountId} FOR UPDATE", cancellationToken);

        var account = await db.Accounts.SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminBanOutcome.AccountNotFound);
        }

        if (await db.BanRecords.AsNoTracking().AnyAsync(
                item => item.AccountId == accountId && item.RevokedAt == null, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminBanOutcome.AlreadyBanned);
        }

        var targetUser = await userManager.FindByIdAsync(account.IdentityUserId.ToString());
        if (targetUser is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminBanOutcome.AccountNotFound);
        }

        db.BanRecords.Add(BanRecord.Create(
            Guid.NewGuid(), accountId, reason, now, actorIdentityUserId, internalNote));
        audit.Add(actorIdentityUserId, now, AuditEventType, accountId);

        try
        {
            var stampResult = await userManager.UpdateSecurityStampAsync(targetUser);
            if (!stampResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(AdminBanOutcome.SecurityStampUpdateFailed);
            }

            await transaction.CommitAsync(cancellationToken);
            return new(AdminBanOutcome.Succeeded);
        }
        catch (DbUpdateException exception) when (IsActiveBanUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(AdminBanOutcome.AlreadyBanned);
        }
    }

    public async Task<AdminUnbanResult> UnbanAsync(
        Guid actorIdentityUserId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if (actorIdentityUserId == Guid.Empty || accountId == Guid.Empty)
        {
            return new(AdminUnbanOutcome.AccountNotFound);
        }

        var now = clock.UtcNow.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Use the same per-account lock as ban so simultaneous ban/unban commands serialize.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM accounts WHERE id = {accountId} FOR UPDATE", cancellationToken);

        var account = await db.Accounts.SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminUnbanOutcome.AccountNotFound);
        }

        var activeBans = await db.BanRecords
            .Where(item => item.AccountId == accountId && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        if (activeBans.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminUnbanOutcome.NoActiveBan);
        }

        // The partial unique index guarantees at most one active ban. Fail closed if the
        // database invariant was bypassed or damaged rather than revoking an arbitrary row.
        if (activeBans.Count != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Account has multiple active ban records.");
        }

        var targetUser = await userManager.FindByIdAsync(account.IdentityUserId.ToString());
        if (targetUser is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminUnbanOutcome.AccountNotFound);
        }

        activeBans[0].Revoke(now);
        audit.Add(actorIdentityUserId, now, UnbanAuditEventType, accountId);

        var stampResult = await userManager.UpdateSecurityStampAsync(targetUser);
        if (!stampResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AdminUnbanOutcome.SecurityStampUpdateFailed);
        }

        await transaction.CommitAsync(cancellationToken);
        return new(AdminUnbanOutcome.Succeeded);
    }

    private static bool IsActiveBanUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_ban_records_one_active_per_account"
        };
}
