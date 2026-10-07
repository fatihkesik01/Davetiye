using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>
/// Anonymous Memories surface (P6-M2: text/emoji only). Every gate failure (unknown/malformed code, not
/// effective-Active incl. Scheduled, module off, entitlement off) collapses to one NotFound outcome so the
/// response never discloses which gate failed. No guest identity, IP or user agent is read or stored.
/// </summary>
public sealed class PublicMemoriesService(
    DavetiyeDbContext dbContext,
    IMemoriesGuestInvitationAccessReader invitationAccess,
    IEffectiveEntitlementResolver entitlements,
    IGuestMemoryMediaDeliveryService mediaDelivery,
    IGuestMediaAssetStatusReader mediaAssetStatuses,
    IGuestMediaUploadAvailability mediaUploadAvailability,
    IClock clock,
    IPublicMemorySubmissionLimiter submissionLimiter,
    IOptions<MemoryInputLimits> limitsOptions) : IPublicMemoriesService
{
    private readonly MemoryInputLimits limits = limitsOptions.Value;

    public async Task<PublicMemoriesConfigurationResult> GetConfigurationAsync(string publicCode,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicMemoryOutcome.NotFound);
        var entitlement = await ResolveEntitlementAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (!MemoryProjectionPolicy.CanAcceptSubmission(config?.IsEnabled ?? false, entitlement?.MemoriesEnabled == true, invitationEffectivelyActive: true))
            return new(PublicMemoryOutcome.NotFound);
        var upload = GuestUploadLimits(entitlement!, mediaUploadAvailability.IsAvailable);
        return new(PublicMemoryOutcome.Available, new PublicMemoriesConfiguration("available",
            new PublicMemoryInputLimits(limits.MaxDisplayNameCharacters, limits.MaxTextCharacters, limits.MaxEmojiCharacters), upload));
    }

    public async Task<PublicMemoriesListResult> ListAsync(string publicCode, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicMemoryOutcome.NotFound);
        var entitlement = await ResolveEntitlementAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (config is null ||
            !MemoryProjectionPolicy.IsVisibleToPublic(MemoryState.Published, config.IsEnabled, config.Visibility, entitlement?.MemoriesEnabled == true))
            return new(PublicMemoryOutcome.NotFound);

        // Phase 6's accepted projection shows text immediately for PendingMedia; media-only PendingMedia rows
        // appear only when at least one linked Guest asset is Ready. Load candidates first so filtering happens
        // before count/pagination and no empty/pending-only rows affect the public total.
        var rows = await dbContext.Memories.AsNoTracking()
            .Where(memory => memory.InvitationId == access.InvitationId &&
                (memory.State == MemoryState.Published || memory.State == MemoryState.PendingMedia))
            .Select(memory => new { memory.Id, memory.State, memory.DisplayName, memory.Text, memory.Emoji, memory.CreatedAt })
            .ToListAsync(cancellationToken);
        var memoryIds = rows.Select(row => row.Id).ToArray();
        var links = memoryIds.Length == 0
            ? []
            : await dbContext.MemoryMedia.AsNoTracking().Where(link => memoryIds.Contains(link.MemoryId))
                .OrderBy(link => link.MemoryId).ThenBy(link => link.Ordinal)
                .Select(link => new { link.MemoryId, link.MediaAssetId }).ToListAsync(cancellationToken);
        IReadOnlyList<GuestMediaAssetStatus> statuses = links.Count == 0
            ? Array.Empty<GuestMediaAssetStatus>()
            : await mediaAssetStatuses.ListAsync(access.InvitationId,
                links.Select(link => link.MediaAssetId).Distinct().ToArray(), cancellationToken);
        var statusByAsset = statuses.Where(status => MemoryProjectionPolicy.IsMediaVisible(status.Readiness == GuestMediaAssetReadiness.Ready))
            .ToDictionary(status => status.AssetId);
        var mediaByMemory = links.Where(link => statusByAsset.ContainsKey(link.MediaAssetId))
            .GroupBy(link => link.MemoryId).ToDictionary(group => group.Key,
                group => (IReadOnlyList<PublicMemoryMediaItem>)group.Select(link =>
                    new PublicMemoryMediaItem(link.MediaAssetId, statusByAsset[link.MediaAssetId].Kind.ToString())).ToArray());
        var visibleRows = rows.Where(row => row.Text is not null || row.Emoji is not null || mediaByMemory.ContainsKey(row.Id))
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id).ToArray();
        var total = visibleRows.Length;
        // Offset in long: an absurd page number must yield an empty page, never an int overflow or a database error.
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= total
            ? []
            : visibleRows.Skip((int)offset).Take(pageSize).Select(row => new PublicMemoryItem(row.Id, row.DisplayName,
                row.Text, row.Emoji, TruncateToMinute(row.CreatedAt),
                mediaByMemory.GetValueOrDefault(row.Id, Array.Empty<PublicMemoryMediaItem>()))).ToList();
        return new(PublicMemoryOutcome.Available, new PublicMemoriesPage(page, pageSize, total, items));
    }

    public async Task<PublicMemoryDeliveryResult> CreateMediaDeliveryAsync(string publicCode, Guid memoryId, Guid assetId,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null || !await IsPubliclyVisibleAsync(access, memoryId, assetId, cancellationToken))
            return new(PublicMemoryOutcome.NotFound);

        var issued = await mediaDelivery.CreateAsync(access.InvitationId, assetId, cancellationToken);
        if (issued.Outcome == "Unavailable") return new(PublicMemoryOutcome.Unavailable);
        if (issued.Outcome != "Succeeded" || issued.Url is null || issued.ExpiresAt is null)
            return new(PublicMemoryOutcome.NotFound);

        // Re-evaluate invitation, entitlement, visibility, publication and link after the provider call.
        access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null || !await IsPubliclyVisibleAsync(access, memoryId, assetId, cancellationToken) ||
            !await mediaDelivery.IsReadyForInvitationAsync(access.InvitationId, assetId, cancellationToken))
            return new(PublicMemoryOutcome.NotFound);
        return new(PublicMemoryOutcome.Available, issued.MediaKind, issued.Url, issued.ExpiresAt);
    }

    public async Task<PublicMemorySubmitResult> SubmitAsync(string publicCode, SubmitPublicMemoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var name, out var text, out var emoji, out var invalid)) return invalid!;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Locks the invitation row: serializes concurrent submissions so the cap below cannot be raced.
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicMemoryOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return new(PublicMemoryOutcome.NotFound);
        var entitlement = await ResolveEntitlementAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (!MemoryProjectionPolicy.CanAcceptSubmission(config?.IsEnabled ?? false, entitlement?.MemoriesEnabled == true, invitationEffectivelyActive: true))
            return new(PublicMemoryOutcome.NotFound);

        // Per-invitation abuse limit; consulted only after the gate so it cannot be used to probe public codes.
        if (!submissionLimiter.TryAcquire(access.InvitationId)) return new(PublicMemoryOutcome.RateLimited);

        var count = await dbContext.Memories.CountAsync(memory =>
            memory.InvitationId == access.InvitationId && memory.State != MemoryState.Abandoned, cancellationToken);
        if (count >= limits.MaxMemoriesPerInvitation) return new(PublicMemoryOutcome.QuotaReached);

        var memory = Memory.Create(Guid.NewGuid(), access.InvitationId, name, text, emoji, expectsMedia: false, now);
        dbContext.Memories.Add(memory);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicMemoryOutcome.Available, new PublicMemorySubmissionResponse(memory.Id, TruncateToMinute(memory.CreatedAt)));
    }

    /// <summary>Public timestamps are coarsened to the minute; full precision stays internal.</summary>
    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    private bool TryValidate(SubmitPublicMemoryRequest request, out string? name, out string? text, out string? emoji,
        out PublicMemorySubmitResult? invalid)
    {
        name = text = emoji = null;
        invalid = null;
        if (!MemoryTextPolicy.TryNormalize(request.DisplayName, MemoryTextPolicy.Field.DisplayName, out name, out var error))
            return Fail("displayName", error!, out invalid);
        if (!MemoryTextPolicy.TryNormalize(request.Text, MemoryTextPolicy.Field.Text, out text, out error))
            return Fail("text", error!, out invalid);
        if (!MemoryTextPolicy.TryNormalize(request.Emoji, MemoryTextPolicy.Field.Emoji, out emoji, out error))
            return Fail("emoji", error!, out invalid);
        var message = limits.ValidateSubmission(name, text, emoji, mediaCount: 0);
        if (message is null) return true;
        var key = name is not null && name.Length > limits.MaxDisplayNameCharacters ? "displayName"
            : text is not null && text.Length > limits.MaxTextCharacters ? "text"
            : emoji is not null && emoji.Length > limits.MaxEmojiCharacters ? "emoji" : "memory";
        return Fail(key, message, out invalid);
    }

    private static bool Fail(string key, string message, out PublicMemorySubmitResult? invalid)
    {
        invalid = new(PublicMemoryOutcome.Invalid, Errors: new Dictionary<string, string[]> { [key] = [message] });
        return false;
    }

    private async Task<EffectiveEntitlementSnapshot?> ResolveEntitlementAsync(InvitationMemoriesGuestAccess access, CancellationToken cancellationToken)
    {
        var result = await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.MemorySubmission), cancellationToken);
        return result.IsGranted ? result.Entitlements : null;
    }

    private async Task<bool> IsPubliclyVisibleAsync(InvitationMemoriesGuestAccess access, Guid memoryId, Guid assetId,
        CancellationToken cancellationToken)
    {
        var entitlement = await ResolveEntitlementAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (!MemoryProjectionPolicy.IsVisibleToPublic(MemoryState.Published, config?.IsEnabled ?? false,
                config?.Visibility ?? MemoryVisibility.CreatorOnly, entitlement?.MemoriesEnabled == true)) return false;
        return await (from memory in dbContext.Memories.AsNoTracking()
                      join link in dbContext.MemoryMedia.AsNoTracking() on memory.Id equals link.MemoryId
                      where memory.Id == memoryId && memory.InvitationId == access.InvitationId &&
                            (memory.State == MemoryState.Published ||
                             (memory.State == MemoryState.PendingMedia &&
                              (memory.Text != null || memory.Emoji != null || link.MediaAssetId == assetId))) &&
                            link.MediaAssetId == assetId
                      select memory.Id).AnyAsync(cancellationToken);
    }

    private PublicMemoriesGuestUploadLimits GuestUploadLimits(EffectiveEntitlementSnapshot entitlement, bool mediaProviderAvailable)
    {
        var images = mediaProviderAvailable && entitlement.MaxGuestImages > 0 && entitlement.MaxGuestImageSizeMb > 0;
        var videos = mediaProviderAvailable && entitlement.MaxGuestVideos > 0 && entitlement.MaxGuestVideoSizeMb > 0 && entitlement.MaxGuestVideoDurationSeconds > 0;
        return new(images || videos, limits.MaxMediaPerMemory, images ? entitlement.MaxGuestImages : 0, videos ? entitlement.MaxGuestVideos : 0,
            images ? entitlement.MaxGuestImageSizeMb : 0, videos ? entitlement.MaxGuestVideoSizeMb : 0,
            videos ? entitlement.MaxGuestVideoDurationSeconds : 0);
    }

    private Task<MemoryConfiguration?> LoadConfigurationAsync(Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.MemoryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
}
