namespace Davetiye.Application.Modules.Analytics.Contracts;

/// <summary>Aggregate render counts only; no visitor or session information is accepted.</summary>
public interface IInvitationViewCounter
{
    Task RecordAsync(Guid invitationId, CancellationToken cancellationToken);
    Task<long> ReadAsync(Guid invitationId, CancellationToken cancellationToken);
}
