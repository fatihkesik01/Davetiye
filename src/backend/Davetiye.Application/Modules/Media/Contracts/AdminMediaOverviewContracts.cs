namespace Davetiye.Application.Modules.Media.Contracts;

public sealed record AdminMediaOverview(
    long Assets,
    long Ready,
    long PendingUpload,
    long Processing,
    long PendingDeletion,
    long Deleted,
    long Rejected,
    long VerifiedBytes);

public interface IAdminMediaOverviewReader
{
    Task<AdminMediaOverview> GetAsync(CancellationToken cancellationToken);
}
