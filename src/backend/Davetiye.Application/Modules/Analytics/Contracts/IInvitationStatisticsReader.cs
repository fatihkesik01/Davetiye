namespace Davetiye.Application.Modules.Analytics.Contracts;

public interface IInvitationStatisticsReader
{
    Task<InvitationAggregateStatistics> ReadAsync(Guid invitationId, CancellationToken cancellationToken);
}
