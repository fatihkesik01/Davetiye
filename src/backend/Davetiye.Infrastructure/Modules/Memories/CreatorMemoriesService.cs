using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>Owner-scoped moderation of finalized Guest memories.</summary>
public sealed class CreatorMemoriesService(DavetiyeDbContext dbContext,
    IMemoriesCreatorInvitationAccessReader invitationAccessReader, IGuestMediaAssetStatusReader mediaStatuses,
    IGuestMediaUploadService guestMedia, IGuestMemoryMediaDeliveryService mediaDelivery, IClock clock) : ICreatorMemoriesService
{
    public async Task<CreatorMemoriesListResult> ListAsync(Guid accountId, Guid invitationId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || invitationId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            return new(CreatorMemoriesOutcome.NotFound);
        if (await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorMemoriesOutcome.NotFound);

        var rows = await dbContext.Memories.AsNoTracking()
            .Where(memory => memory.InvitationId == invitationId &&
                (memory.State == MemoryState.Published || memory.State == MemoryState.Hidden))
            .OrderByDescending(memory => memory.CreatedAt).ThenBy(memory => memory.Id)
            .Select(memory => new { memory.Id, memory.DisplayName, memory.Text, memory.Emoji, memory.State, memory.CreatedAt })
            .ToListAsync(cancellationToken);
        var memoryIds = rows.Select(row => row.Id).ToArray();
        var links = memoryIds.Length == 0 ? [] : await dbContext.MemoryMedia.AsNoTracking()
            .Where(link => memoryIds.Contains(link.MemoryId))
            .OrderBy(link => link.MemoryId).ThenBy(link => link.Ordinal)
            .Select(link => new { link.MemoryId, link.MediaAssetId, link.Ordinal })
            .ToListAsync(cancellationToken);
        var statuses = links.Count == 0 ? Array.Empty<GuestMediaAssetStatus>()
            : await mediaStatuses.ListAsync(invitationId, links.Select(link => link.MediaAssetId).Distinct().ToArray(), cancellationToken);
        var statusByAsset = statuses.ToDictionary(status => status.AssetId);
        var mediaByMemory = links.Where(link => statusByAsset.ContainsKey(link.MediaAssetId))
            .GroupBy(link => link.MemoryId).ToDictionary(group => group.Key,
                group => (IReadOnlyList<CreatorMemoryMediaItem>)group.OrderBy(link => link.Ordinal).Select(link =>
                {
                    var status = statusByAsset[link.MediaAssetId];
                    return new CreatorMemoryMediaItem(link.MediaAssetId, status.Kind.ToString(), MapStatus(status.Readiness));
                }).ToArray());

        var total = rows.Count;
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= total ? [] : rows.Skip((int)offset).Take(pageSize).Select(row =>
            new CreatorMemoryItem(row.Id, row.DisplayName, row.Text, row.Emoji, row.State.ToString(), row.CreatedAt,
                mediaByMemory.GetValueOrDefault(row.Id, Array.Empty<CreatorMemoryMediaItem>()))).ToArray();
        return new(CreatorMemoriesOutcome.Succeeded, new CreatorMemoriesPage(page, pageSize, total, items));
    }

    public async Task<CreatorMemoryModerationResult> HideAsync(Guid accountId, Guid invitationId, Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || invitationId == Guid.Empty || memoryId == Guid.Empty)
            return new(CreatorMemoriesOutcome.NotFound);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await invitationAccessReader.LockOwnedAndGetEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorMemoriesOutcome.NotFound);
        var memory = await dbContext.Memories.SingleOrDefaultAsync(item => item.Id == memoryId &&
            item.InvitationId == invitationId, cancellationToken);
        if (memory is null) return new(CreatorMemoriesOutcome.NotFound);
        if (memory.State != MemoryState.Published) return new(CreatorMemoriesOutcome.Conflict);

        var now = clock.UtcNow.ToUniversalTime();
        if (memory.FinalizedAt is { } finalizedAt && finalizedAt > now) now = finalizedAt;
        memory.Hide(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorMemoriesOutcome.Succeeded);
    }

    public async Task<CreatorMemoryModerationResult> DeleteAsync(Guid accountId, Guid invitationId, Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || invitationId == Guid.Empty || memoryId == Guid.Empty)
            return new(CreatorMemoriesOutcome.NotFound);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await invitationAccessReader.LockOwnedAndGetEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorMemoriesOutcome.NotFound);
        var memory = await dbContext.Memories.Include(item => item.Media).SingleOrDefaultAsync(item =>
            item.Id == memoryId && item.InvitationId == invitationId, cancellationToken);
        if (memory is null) return new(CreatorMemoriesOutcome.NotFound);
        if (memory.State is not (MemoryState.Published or MemoryState.Hidden))
            return new(CreatorMemoriesOutcome.NotFound);

        var assetIds = memory.Media.Select(link => link.MediaAssetId).ToArray();
        // Media closes Guest upload intents and queues retryable deletion for every live asset in this transaction.
        // Memory never reads Media's tables or provider references directly.
        await guestMedia.DeleteOwnerAssetsAsync(invitationId, assetIds, cancellationToken);
        var capabilities = await dbContext.MemoryUploadCapabilities.Where(item => item.MemoryId == memoryId)
            .ToListAsync(cancellationToken);
        dbContext.MemoryUploadCapabilities.RemoveRange(capabilities);
        dbContext.Memories.Remove(memory); // owned links cascade; capability and memory removal commit with Media discard.
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorMemoriesOutcome.Succeeded);
    }

    public async Task<CreatorMemoryDeliveryResult> CreateMediaDeliveryAsync(Guid accountId, Guid invitationId,
        Guid memoryId, Guid assetId, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || invitationId == Guid.Empty || memoryId == Guid.Empty || assetId == Guid.Empty ||
            await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken) is null ||
            !await HasModeratableLinkAsync(invitationId, memoryId, assetId, cancellationToken))
            return new(CreatorMemoriesOutcome.NotFound);

        var issued = await mediaDelivery.CreateAsync(invitationId, assetId, cancellationToken);
        if (issued.Outcome == "Unavailable") return new(CreatorMemoriesOutcome.Unavailable);
        if (issued.Outcome != "Succeeded" || issued.Url is null || issued.ExpiresAt is null)
            return new(CreatorMemoriesOutcome.NotFound);

        if (await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken) is null ||
            !await HasModeratableLinkAsync(invitationId, memoryId, assetId, cancellationToken) ||
            !await mediaDelivery.IsReadyForInvitationAsync(invitationId, assetId, cancellationToken))
            return new(CreatorMemoriesOutcome.NotFound);
        return new(CreatorMemoriesOutcome.Succeeded, issued.MediaKind, issued.Url, issued.ExpiresAt);
    }

    private Task<bool> HasModeratableLinkAsync(Guid invitationId, Guid memoryId, Guid assetId,
        CancellationToken cancellationToken) =>
        (from memory in dbContext.Memories.AsNoTracking()
         join link in dbContext.MemoryMedia.AsNoTracking() on memory.Id equals link.MemoryId
         where memory.Id == memoryId && memory.InvitationId == invitationId &&
               (memory.State == MemoryState.Published || memory.State == MemoryState.Hidden) &&
               link.MediaAssetId == assetId
         select memory.Id).AnyAsync(cancellationToken);

    private static string MapStatus(GuestMediaAssetReadiness readiness) => readiness switch
    {
        GuestMediaAssetReadiness.Pending => "Pending",
        GuestMediaAssetReadiness.Ready => "Ready",
        GuestMediaAssetReadiness.Rejected => "Rejected",
        _ => "Deleted"
    };
}
