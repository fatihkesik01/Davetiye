using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Davetiye.Infrastructure.Modules.Invitations;

/// <summary>Invitation-owned adapter for Gift Registry Creator ownership and transaction locking.</summary>
public sealed class GiftCreatorInvitationAccessReader(DavetiyeDbContext dbContext) : IGiftCreatorInvitationAccessReader
{
    public Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.Invitations.AsNoTracking().AnyAsync(value => value.Id == invitationId && value.AccountId == accountId && value.DeletedAt == null,
            cancellationToken);

    public async Task<bool> LockOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Gift item mutation requires an active transaction.");
        command.CommandText = "SELECT id FROM invitations WHERE id = @invitationId AND account_id = @accountId AND deleted_at IS NULL FOR UPDATE";
        var invitationParameter = command.CreateParameter(); invitationParameter.ParameterName = "@invitationId"; invitationParameter.Value = invitationId;
        var accountParameter = command.CreateParameter(); accountParameter.ParameterName = "@accountId"; accountParameter.Value = accountId;
        command.Parameters.Add(invitationParameter); command.Parameters.Add(accountParameter);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
