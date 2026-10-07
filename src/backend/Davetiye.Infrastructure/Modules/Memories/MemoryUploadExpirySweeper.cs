using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>
/// Abandons PendingMedia memories whose upload capability expired (P6-M3). Each memory is handled in its own
/// transaction under a row lock; its Media assets are discarded through the Media port (open uploads become Rejected,
/// Ready ones PendingDeletion). Provider bytes are kept until permanent purge and the guest quota stays consumed (PD-16).
/// </summary>
public sealed class MemoryUploadExpirySweeper(DavetiyeDbContext dbContext, IGuestMediaUploadService guestMedia,
    IAbandonedMemoryRetentionSettingsReader retentionSettings, IClock clock)
    : IMemoryUploadExpirySweeper
{
    public async Task<int> SweepAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.UtcNow.ToUniversalTime();
        var candidates = await dbContext.Memories.AsNoTracking()
            .Where(memory => memory.State == MemoryState.PendingMedia &&
                             !dbContext.MemoryUploadCapabilities.Any(capability => capability.MemoryId == memory.Id &&
                                 capability.ConsumedAt == null && capability.RevokedAt == null && capability.ExpiresAt > now))
            .OrderBy(memory => memory.CreatedAt).ThenBy(memory => memory.Id)
            .Select(memory => memory.Id).Take(batchSize).ToListAsync(cancellationToken);

        var abandoned = 0;
        foreach (var memoryId in candidates)
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM memories WHERE id = {memoryId} FOR UPDATE", cancellationToken);
            var memory = await dbContext.Memories.Include(item => item.Media)
                .SingleOrDefaultAsync(item => item.Id == memoryId && item.State == MemoryState.PendingMedia, cancellationToken);
            if (memory is null) continue;
            var capabilities = await dbContext.MemoryUploadCapabilities
                .Where(capability => capability.MemoryId == memoryId && capability.ConsumedAt == null && capability.RevokedAt == null)
                .ToListAsync(cancellationToken);
            if (capabilities.Any(capability => capability.IsUsableAt(now))) continue;

            foreach (var capability in capabilities) capability.Revoke(now);
            var assetIds = memory.Media.Select(item => item.MediaAssetId).ToArray();
            memory.Abandon();
            await dbContext.SaveChangesAsync(cancellationToken);
            await guestMedia.DiscardAsync(memory.InvitationId, assetIds, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            abandoned++;
        }

        dbContext.ChangeTracker.Clear();
        await CleanupOldAbandonedAsync(batchSize, now, cancellationToken);
        dbContext.ChangeTracker.Clear();
        return abandoned;
    }

    private async Task CleanupOldAbandonedAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var retentionDays = await retentionSettings.ReadDaysAsync(cancellationToken);
        if (retentionDays is null) return; // Missing/invalid setting fails closed: retain metadata.
        var cutoff = now.AddDays(-retentionDays.Value);
        var candidates = await dbContext.Memories.AsNoTracking()
            .Where(memory => memory.State == MemoryState.Abandoned &&
                dbContext.MemoryUploadCapabilities.Any(capability => capability.MemoryId == memory.Id &&
                    (capability.RevokedAt ?? capability.ExpiresAt) <= cutoff))
            .OrderBy(memory => memory.CreatedAt).ThenBy(memory => memory.Id)
            .Select(memory => memory.Id).Take(batchSize).ToListAsync(cancellationToken);

        foreach (var memoryId in candidates)
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM memories WHERE id = {memoryId} FOR UPDATE", cancellationToken);
            var memory = await dbContext.Memories.Include(item => item.Media)
                .SingleOrDefaultAsync(item => item.Id == memoryId && item.State == MemoryState.Abandoned, cancellationToken);
            if (memory is null) continue;

            var capabilities = await dbContext.MemoryUploadCapabilities
                .Where(capability => capability.MemoryId == memoryId).ToListAsync(cancellationToken);
            if (capabilities.Count == 0 || capabilities.Max(capability => capability.RevokedAt ?? capability.ExpiresAt) > cutoff)
                continue;

            // These are Memories-owned metadata/link rows only. Guest MediaAsset and PendingUpload rows remain
            // under Media ownership and retain provider bytes/quota until permanent invitation purge (PD-16).
            dbContext.MemoryUploadCapabilities.RemoveRange(capabilities);
            dbContext.Memories.Remove(memory); // MemoryMedia links cascade with the memory.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
