using System.Data;
using System.Text.Json;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class InvitationTrashService(
    DavetiyeDbContext dbContext, IAccountQuotaTransactionRunner runner,
    IAccountReferenceValidator accounts, IInvitationRetentionSettingsReader retention,
    IPublicationGrantLifecycleService grants, IPublicationLifecycleService publications,
    IClock clock) : IInvitationTrashService
{
    public async Task<InvitationTrashPage> ListAsync(Guid accountId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (await accounts.GetStatusAsync(accountId, cancellationToken) != AccountReferenceStatus.Verified)
            throw new UnauthorizedAccessException();
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var query = dbContext.Invitations.IgnoreQueryFilters().AsNoTracking()
            .Where(invitation => invitation.AccountId == accountId && invitation.DeletedAt != null && invitation.PurgeAfter != null);
        var total = await query.CountAsync(cancellationToken);
        var invitations = await query.OrderByDescending(invitation => invitation.DeletedAt).ThenBy(invitation => invitation.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<InvitationTrashItem>();
        foreach (var invitation in invitations)
        {
            var aggregate = (await LoadAsync(accountId, invitation.Id, tracking: false, cancellationToken))!;
            items.Add(Item(aggregate));
        }

        return new(items, page, pageSize, total, clock.UtcNow.ToUniversalTime());
    }

    public Task<InvitationTrashResult> DeleteAsync(Guid accountId, Guid invitationId,
        InvitationTrashRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, invitationId, request, restore: false, cancellationToken);

    public Task<InvitationTrashResult> RestoreAsync(Guid accountId, Guid invitationId,
        InvitationTrashRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(accountId, invitationId, request, restore: true, cancellationToken);

    private async Task<InvitationTrashResult> ExecuteAsync(Guid accountId, Guid invitationId,
        InvitationTrashRequest request, bool restore, CancellationToken cancellationToken)
    {
        if (request?.Expected is null || (!restore && (request.ExpectedRetentionDays is null or < 0 or > 365)) ||
            request.Expected.InvitationRevision < 0 ||
            request.Expected.WorkingContentRevision < 0 || request.Expected.PublishedContentRevision < 0 ||
            request.Expected.WindowRevision < 0 || request.Expected.WindowId == Guid.Empty ||
            (request.Expected.WindowId is null) != (request.Expected.WindowRevision is null))
            return new("InvalidRequest");
        try
        {
            return await runner.ExecuteAsync(accountId, async token =>
            {
                if (!await InitialPublicationStore.LockOwnedInvitationAndWorkingContentAsync(
                        dbContext, accountId, invitationId, token, includeDeleted: true))
                    return new InvitationTrashResult("NotFound");
                var aggregate = (await LoadAsync(accountId, invitationId, tracking: true, token))!;
                if (await accounts.GetStatusAsync(accountId, token) != AccountReferenceStatus.Verified)
                    return new InvitationTrashResult("AccountInactive");
                var expected = Revisions(aggregate);
                if (expected != request.Expected)
                    return new InvitationTrashResult("Conflict", CurrentExpected: expected);
                var invitation = aggregate.Invitation;
                if (restore)
                {
                    var now = clock.UtcNow.ToUniversalTime();
                    if (invitation.DeletedAt is null || invitation.PurgeAfter is null)
                        return new InvitationTrashResult("NotFound");
                    var acceptedPurgeAfter = invitation.PurgeAfter.Value;
                    if (now >= invitation.PurgeAfter) return new InvitationTrashResult("RestoreExpired");
                    if (aggregate.Window is not null && aggregate.Window.StartsAt <= now)
                        await grants.ConsumeStartedAsync(accountId, invitationId, aggregate.Window.GrantId,
                            aggregate.Window.StartsAt, token);
                    now = clock.UtcNow.ToUniversalTime();
                    if (now >= invitation.PurgeAfter)
                        throw new TrashAbort(new("RestoreExpired"));
                    // Restore never brings back stored Active/Scheduled. An expired old window
                    // remains historical; a remaining window is only usable by explicit republish.
                    if (aggregate.Window is not null && aggregate.Window.EndsAt <= now)
                        aggregate.Window.MarkHistorical();
                    invitation.RestoreFromTrash();
                    await dbContext.SaveChangesAsync(token);
                    var status = await publications.GetAsync(accountId, invitationId, token);
                    if (status.Code != "Succeeded") throw new TrashAbort(new(status.Code));
                    if (clock.UtcNow.ToUniversalTime() >= acceptedPurgeAfter)
                        throw new TrashAbort(new("RestoreExpired"));
                    return new InvitationTrashResult("Succeeded", Status: status.Status);
                }

                if (invitation.DeletedAt is not null)
                    return invitation.PurgeAfter is null ? new("InvalidState") : new("Succeeded", Item: Item(aggregate));
                var days = await retention.ReadDaysAsync(token);
                if (days is null) return new InvitationTrashResult("InvalidConfiguration");
                if (request.ExpectedRetentionDays != days)
                    return new InvitationTrashResult("RetentionChanged", CurrentExpected: expected);
                var deletedAt = clock.UtcNow.ToUniversalTime();
                if (aggregate.Window is not null && aggregate.Window.StartsAt > deletedAt)
                {
                    if (invitation.State != InvitationStoredState.Scheduled)
                        return new InvitationTrashResult("InvalidState");
                    if (!await grants.ReleaseAsync(accountId, invitationId, aggregate.Window.GrantId, token))
                        throw new TrashAbort(new("GrantUnavailable"));
                    // A delayed release must not commit after the accepted start. Retrying then
                    // follows the started-window path and preserves consumption permanently.
                    deletedAt = clock.UtcNow.ToUniversalTime();
                    if (aggregate.Window.StartsAt <= deletedAt)
                        throw new TrashAbort(new("InvalidState"));
                    aggregate.Window.MarkHistorical();
                    invitation.ChangePublicationState(InvitationStoredState.Draft);
                }
                else if (aggregate.Window is not null)
                    await grants.ConsumeStartedAsync(accountId, invitationId, aggregate.Window.GrantId,
                        aggregate.Window.StartsAt, token);
                deletedAt = clock.UtcNow.ToUniversalTime();
                invitation.MoveToTrash(deletedAt, deletedAt.AddDays(days.Value));
                await dbContext.SaveChangesAsync(token);
                return new InvitationTrashResult("Succeeded", Item: Item(aggregate));
            }, cancellationToken);
        }
        catch (TrashAbort exception) { return exception.Result; }
        catch (DbUpdateConcurrencyException) { return new("Conflict"); }
    }

    private async Task<Aggregate?> LoadAsync(Guid accountId, Guid invitationId, bool tracking,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Invitations.IgnoreQueryFilters()
            .AsTracking(tracking ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking)
            .SingleOrDefaultAsync(candidate => candidate.AccountId == accountId && candidate.Id == invitationId, cancellationToken);
        if (invitation is null) return null;
        var working = await dbContext.WorkingContents
            .AsTracking(tracking ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking)
            .SingleAsync(candidate => candidate.InvitationId == invitationId, cancellationToken);
        var published = await dbContext.PublishedContents
            .AsTracking(tracking ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking)
            .SingleOrDefaultAsync(candidate => candidate.InvitationId == invitationId, cancellationToken);
        var window = await dbContext.PublicationWindows
            .AsTracking(tracking ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking)
            .SingleOrDefaultAsync(candidate => candidate.InvitationId == invitationId && candidate.IsCurrent, cancellationToken);
        if (tracking)
        {
            await dbContext.Entry(invitation).ReloadAsync(cancellationToken);
            await dbContext.Entry(working).ReloadAsync(cancellationToken);
            if (published is not null) await dbContext.Entry(published).ReloadAsync(cancellationToken);
            if (window is not null) await dbContext.Entry(window).ReloadAsync(cancellationToken);
        }

        return new(invitation, working, published, window);
    }

    private static InvitationTrashItem Item(Aggregate aggregate)
    {
        string? headline = null;
        try
        {
            using var content = JsonDocument.Parse(aggregate.Working.Content);
            if (content.RootElement.ValueKind == JsonValueKind.Object &&
                content.RootElement.TryGetProperty("headline", out var value) && value.ValueKind == JsonValueKind.String)
                headline = value.GetString();
        }
        catch (JsonException) { }
        return new(aggregate.Invitation.Id, headline, aggregate.Invitation.DeletedAt!.Value,
            aggregate.Invitation.PurgeAfter!.Value, Revisions(aggregate));
    }

    private static PublicationRevisions Revisions(Aggregate aggregate) => new(aggregate.Invitation.Revision,
        aggregate.Working.Revision, aggregate.Published?.Revision, aggregate.Window?.Id, aggregate.Window?.Revision);
    private sealed record Aggregate(Invitation Invitation, WorkingContent Working, PublishedContent? Published,
        PublicationWindow? Window);
    private sealed class TrashAbort(InvitationTrashResult result) : Exception("Trash transaction aborted.")
    {
        public InvitationTrashResult Result { get; } = result;
    }
}
