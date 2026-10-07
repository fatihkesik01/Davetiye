namespace Davetiye.Application.Modules.Invitations.Contracts;

/// <summary>Invitation-owned, owner-scoped context needed to decide whether private Creator media may be issued.</summary>
public sealed record CreatorMediaInvitationAccessSnapshot(
    bool CanIssueCreatorMediaIntent,
    Guid? CurrentGrantId,
    string? TemplateKey);

public interface ICreatorMediaInvitationAccessReader
{
    /// <summary>Returns null for missing, foreign-owned, or trashed invitations.</summary>
    Task<CreatorMediaInvitationAccessSnapshot?> LoadAsync(
        Guid accountId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken);
}
