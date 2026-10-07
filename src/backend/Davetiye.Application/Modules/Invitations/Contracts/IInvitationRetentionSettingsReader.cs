namespace Davetiye.Application.Modules.Invitations.Contracts;

public interface IInvitationRetentionSettingsReader
{
    Task<int?> ReadDaysAsync(CancellationToken cancellationToken);
}
