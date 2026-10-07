using System.Text.Json.Serialization;

namespace Davetiye.Application.Modules.Invitations.Contracts;

public sealed record InvitationTrashItem(Guid InvitationId, string? Headline,
    DateTimeOffset DeletedAtUtc, DateTimeOffset PurgeAfterUtc, PublicationRevisions Expected);
public sealed record InvitationTrashPage(IReadOnlyList<InvitationTrashItem> Items,
    int Page, int PageSize, int TotalCount, DateTimeOffset ServerNowUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvitationTrashRequest(PublicationRevisions Expected, int? ExpectedRetentionDays = null);
public sealed record InvitationTrashResult(string Code, InvitationTrashItem? Item = null,
    PublicationStatus? Status = null, PublicationRevisions? CurrentExpected = null);

public interface IInvitationTrashService
{
    Task<InvitationTrashPage> ListAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
    Task<InvitationTrashResult> DeleteAsync(Guid accountId, Guid invitationId, InvitationTrashRequest request,
        CancellationToken cancellationToken);
    Task<InvitationTrashResult> RestoreAsync(Guid accountId, Guid invitationId, InvitationTrashRequest request,
        CancellationToken cancellationToken);
}

public sealed record InvitationLifecycleJobResult(int Scanned, int Activated, int Expired, int Purged);
public interface IInvitationLifecycleJobs
{
    Task<InvitationLifecycleJobResult> RunBatchAsync(int batchSize, CancellationToken cancellationToken);
}
