namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

public sealed record PublicationGrantOption(
    Guid? GrantId, string Label, string Kind, long MaxPublishDays,
    long MaxActiveInvitations, bool PremiumTemplatesEnabled);

/// <summary>Plans-owned operations used inside the account publication transaction.</summary>
public interface IPublicationGrantLifecycleService
{
    Task<IReadOnlyList<PublicationGrantOption>> ListChoicesAsync(
        Guid accountId, Guid invitationId, IReadOnlyCollection<Guid> startedGrantIds,
        CancellationToken cancellationToken);
    Task<bool> ReleaseAsync(Guid accountId, Guid invitationId, Guid grantId,
        CancellationToken cancellationToken);
    Task<bool> ConsumeStartedAsync(Guid accountId, Guid invitationId, Guid grantId,
        DateTimeOffset startsAtUtc, CancellationToken cancellationToken);
}
