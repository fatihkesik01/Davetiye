using System.Data;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class CreatorMediaLibraryService(DavetiyeDbContext db, IAccountReferenceValidator accounts,
    ITemplateSelectionResolver templates, ICreatorMediaInvitationOwnerReader ownerReader, IClock clock) : ICreatorMediaLibraryService
{
    public async Task<CreatorMediaLibraryResult> ListAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (!await IsOwnedAsync(accountId, invitationId, cancellationToken)) return new("NotFound");
        var rows = await (
            from asset in db.MediaAssets.AsNoTracking()
            join intent in db.PendingUploads.AsNoTracking() on asset.Id equals intent.MediaAssetId
            where asset.InvitationId == invitationId && asset.QuotaScope == MediaQuotaScope.Creator
            orderby asset.CreatedAt
            select new { Asset = asset, Intent = intent })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Asset.Id).ToArray();
        var placements = await db.MediaPlacements.AsNoTracking().Where(item => ids.Contains(item.MediaAssetId))
            .OrderBy(item => item.SortOrder).Select(item => new { item.MediaAssetId, item.Role, item.SortOrder })
            .ToListAsync(cancellationToken);
        return new("Succeeded", rows.Select(row => new CreatorMediaAssetView(row.Asset.Id, row.Asset.Kind.ToString(),
            row.Asset.State.ToString(), row.Asset.ByteLength, row.Asset.DurationSeconds, row.Intent.RequestedPresentationRole.ToString(),
            placements.Where(item => item.MediaAssetId == row.Asset.Id)
                .Select(item => new CreatorMediaPlacementView(item.Role.ToString(), item.SortOrder)).ToArray())).ToArray());
    }

    public async Task<CreatorMediaLibraryResult> SetPlacementAsync(CreatorMediaPlacementCommand command, CancellationToken cancellationToken)
    {
        if (command.AccountId == Guid.Empty || command.InvitationId == Guid.Empty || command.AssetId == Guid.Empty ||
            !Enum.IsDefined(command.Role) || command.SortOrder is < 0 or > 1000) return new("InvalidRequest");
        if (!await IsOwnedAsync(command.AccountId, command.InvitationId, cancellationToken)) return new("NotFound");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var templateKey = await ownerReader.GetOwnedTemplateKeyAsync(command.AccountId, command.InvitationId, cancellationToken);
        var template = templateKey is null ? null : await templates.ResolveActiveAsync(templateKey, cancellationToken);
        var module = command.Role == MediaPresentationRole.Cover ? "hero" : "gallery";
        if (template?.SupportedModules.Contains(module, StringComparer.Ordinal) != true) return new("UnsupportedModule");
        var asset = await db.MediaAssets.SingleOrDefaultAsync(item => item.Id == command.AssetId &&
            item.InvitationId == command.InvitationId && item.QuotaScope == MediaQuotaScope.Creator, cancellationToken);
        if (asset is null) return new("NotFound");
        if (asset.State != MediaAssetState.Ready) return new("NotReady");
        var current = await db.MediaPlacements.SingleOrDefaultAsync(item => item.MediaAssetId == asset.Id && item.Role == command.Role, cancellationToken);
        if (current is null)
            db.MediaPlacements.Add(asset.Place(Guid.NewGuid(), command.Role, command.SortOrder, clock.UtcNow.ToUniversalTime()));
        else
            current.ChangeSortOrder(command.SortOrder);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new("Succeeded");
    }

    public async Task<CreatorMediaLibraryResult> DeleteAsync(CreatorMediaDeleteCommand command, CancellationToken cancellationToken)
    {
        if (command.AccountId == Guid.Empty || command.InvitationId == Guid.Empty || command.AssetId == Guid.Empty)
            return new("InvalidRequest");
        if (!await IsOwnedAsync(command.AccountId, command.InvitationId, cancellationToken)) return new("NotFound");
        var asset = await db.MediaAssets.SingleOrDefaultAsync(item => item.Id == command.AssetId &&
            item.InvitationId == command.InvitationId && item.QuotaScope == MediaQuotaScope.Creator, cancellationToken);
        if (asset is null) return new("NotFound");
        if (asset.State is MediaAssetState.Deleted or MediaAssetState.PendingDeletion) return new("Deleting");
        asset.RequestDeletion(clock.UtcNow.ToUniversalTime());
        var workingPlacements = await db.MediaPlacements.Where(item => item.MediaAssetId == asset.Id).ToListAsync(cancellationToken);
        db.MediaPlacements.RemoveRange(workingPlacements);
        await db.SaveChangesAsync(cancellationToken);
        return new("Deleting");
    }

    private async Task<bool> IsOwnedAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || invitationId == Guid.Empty ||
            await accounts.GetStatusAsync(accountId, cancellationToken) != AccountReferenceStatus.Verified) return false;
        return await ownerReader.IsOwnedAsync(accountId, invitationId, cancellationToken);
    }
}
