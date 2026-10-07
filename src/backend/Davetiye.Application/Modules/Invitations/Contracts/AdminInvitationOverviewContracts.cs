namespace Davetiye.Application.Modules.Invitations.Contracts;

public sealed record AdminInvitationOverview(long Draft, long Scheduled, long Active, long Paused, long Expired, long Deleted);

public interface IAdminInvitationOverviewReader
{
    Task<AdminInvitationOverview> GetAsync(CancellationToken cancellationToken);
}
