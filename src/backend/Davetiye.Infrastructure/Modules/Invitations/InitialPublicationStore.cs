using System.Data;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InitialPublicationStore(DavetiyeDbContext dbContext,
    Davetiye.Application.Modules.Media.Contracts.IMediaPublicationSnapshotReader? mediaSnapshots = null) : IInitialPublicationStore
{
    public async Task<InitialPublicationDraft?> LoadOwnedDraftAsync(
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Initial publication must load its snapshot inside the account transaction.");
        }

        if (!await LockOwnedInvitationAndWorkingContentAsync(
                dbContext,
                accountId,
                invitationId,
                cancellationToken))
        {
            return null;
        }

        var invitation = await dbContext.Invitations.SingleAsync(
            candidate => candidate.Id == invitationId && candidate.AccountId == accountId,
            cancellationToken);
        var workingContent = await dbContext.WorkingContents.SingleAsync(
            content => content.InvitationId == invitationId,
            cancellationToken);
        // A long-lived scope may already track older values. Refresh only these locked rows
        // so revision checks and Published content always use the committed database snapshot.
        await dbContext.Entry(invitation).ReloadAsync(cancellationToken);
        await dbContext.Entry(workingContent).ReloadAsync(cancellationToken);
        var hasPublishedContent = await dbContext.PublishedContents
            .AsNoTracking()
            .AnyAsync(content => content.InvitationId == invitationId, cancellationToken);
        var hasCurrentPublicationWindow = await dbContext.PublicationWindows
            .AsNoTracking()
            .AnyAsync(
                window => window.InvitationId == invitationId && window.IsCurrent,
                cancellationToken);
        var media = mediaSnapshots is null ? [] : await mediaSnapshots.ListReadyPlacementsAsync(invitationId, cancellationToken);

        return new InitialPublicationDraft(
            invitation,
            workingContent,
            hasPublishedContent,
            hasCurrentPublicationWindow,
            media);
    }

    public async Task<IReadOnlyList<PublicationQuotaSlot>> ListAccountPublicationSlotsAsync(
        Guid accountId,
        CancellationToken cancellationToken) =>
        await (
                from window in dbContext.PublicationWindows.AsNoTracking()
                join invitation in dbContext.Invitations.AsNoTracking()
                    on window.InvitationId equals invitation.Id
                where invitation.AccountId == accountId &&
                      window.IsCurrent &&
                      (invitation.State == InvitationStoredState.Scheduled ||
                       invitation.State == InvitationStoredState.Active ||
                       invitation.State == InvitationStoredState.Paused)
                select new PublicationQuotaSlot(
                    invitation.Id,
                    window.StartsAt, window.EndsAt))
            .ToListAsync(cancellationToken);

    public async Task<InitialPublicationSaveResult> SaveAsync(
        Invitation invitation,
        PublishedContent publishedContent,
        PublicationWindow publicationWindow,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Initial publication must save inside the account transaction.");
        }

        var invitationEntry = dbContext.Entry(invitation);
        if (invitationEntry.State == EntityState.Detached)
        {
            throw new InvalidOperationException("The locked invitation must remain tracked until publication saves.");
        }

        var existingPublished = await dbContext.PublishedContents.SingleOrDefaultAsync(
            content => content.InvitationId == invitation.Id, cancellationToken);
        if (existingPublished is null)
        {
            dbContext.PublishedContents.Add(publishedContent);
        }
        else
        {
            await dbContext.Entry(existingPublished).ReloadAsync(cancellationToken);
            existingPublished.ReplaceWith(publishedContent);
        }
        dbContext.PublicationWindows.Add(publicationWindow);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new InitialPublicationSaveResult(InitialPublicationSaveOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await TryReadCurrentRevisionsAsync(invitation.Id, cancellationToken);
            return new InitialPublicationSaveResult(
                InitialPublicationSaveOutcome.Conflict,
                current?.InvitationRevision,
                current?.WorkingContentRevision);
        }
        catch (DbUpdateException exception) when (IsCompetingInitialPublication(exception))
        {
            // PostgreSQL marks the transaction failed after a constraint violation, so an
            // authoritative reload is unsafe until the outer runner rolls it back.
            return new InitialPublicationSaveResult(InitialPublicationSaveOutcome.Conflict);
        }
    }

    internal static async Task<bool> LockOwnedInvitationAndWorkingContentAsync(
        DavetiyeDbContext dbContext,
        Guid accountId,
        Guid invitationId,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT 1
            FROM invitations AS invitation
            INNER JOIN working_contents AS working
                ON working.invitation_id = invitation.id
            WHERE invitation.id = @invitation_id
              AND invitation.account_id = @account_id
              AND (invitation.deleted_at IS NULL OR @include_deleted)
            FOR UPDATE OF invitation, working
            """;

        var invitationParameter = command.CreateParameter();
        invitationParameter.ParameterName = "invitation_id";
        invitationParameter.Value = invitationId;
        command.Parameters.Add(invitationParameter);

        var accountParameter = command.CreateParameter();
        accountParameter.ParameterName = "account_id";
        accountParameter.Value = accountId;
        command.Parameters.Add(accountParameter);

        var deletedParameter = command.CreateParameter();
        deletedParameter.ParameterName = "include_deleted";
        deletedParameter.Value = includeDeleted;
        command.Parameters.Add(deletedParameter);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<CurrentRevisions?> TryReadCurrentRevisionsAsync(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        return await (
                from invitation in dbContext.Invitations.AsNoTracking()
                join content in dbContext.WorkingContents.AsNoTracking()
                    on invitation.Id equals content.InvitationId
                where invitation.Id == invitationId
                select new CurrentRevisions(invitation.Revision, content.Revision))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool IsCompetingInitialPublication(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_published_contents_invitation_id" or
                "ux_publication_windows_current_invitation"
        };

    private sealed record CurrentRevisions(long InvitationRevision, long WorkingContentRevision);
}
