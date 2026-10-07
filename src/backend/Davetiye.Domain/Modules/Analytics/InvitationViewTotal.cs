namespace Davetiye.Domain.Modules.Analytics;

public sealed class InvitationViewTotal
{
    public Guid InvitationId { get; private set; }
    public long Total { get; private set; }
}
