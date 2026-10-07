namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Invitations-owned command to place all of an account's invitations directly on purge.</summary>
public interface IInvitationAccountDeletionCommand
{
    Task SchedulePermanentPurgeAsync(Guid accountId, DateTimeOffset scheduledAtUtc,
        CancellationToken cancellationToken);
}
