using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

public sealed class MemoryUploadCapabilityOptions
{
    public const string SectionName = "MemoryUploadCapabilities";
    public string HmacKeyBase64 { get; init; } = string.Empty;
    public int HmacKeyVersion { get; init; } = 1;
}

/// <summary>
/// Anonymous guest memory-with-media workflow (P6-M3). A memory is created in PendingMedia together with one short-lived,
/// single-memory upload capability (only its purpose-scoped HMAC digest is stored; the raw token travels in an HttpOnly
/// cookie). Intent, status and finalize all require that capability. Unknown/expired/consumed/foreign capabilities,
/// another memory or invitation, and any failed gate collapse to one NotFound outcome so nothing is disclosed.
/// No guest identity, IP or user agent is read or stored.
/// </summary>
public sealed class PublicMemoryUploadService(
    DavetiyeDbContext dbContext,
    IMemoriesGuestInvitationAccessReader invitationAccess,
    IEffectiveEntitlementResolver entitlements,
    IGuestMediaUploadService guestMedia,
    IGuestMediaUploadAvailability mediaUploadAvailability,
    IGuestMediaVerificationService guestVerification,
    IGuestMediaAssetStatusReader assetStatuses,
    IClock clock,
    IPublicMemorySubmissionLimiter submissionLimiter,
    IPublicMemoryUploadIntentLimiter intentLimiter,
    IOptions<MemoryUploadCapabilityOptions> capabilityOptions,
    IOptions<MemoryInputLimits> limitsOptions) : IPublicMemoryUploadService
{
    private const long BytesPerMegabyte = 1024L * 1024L;
    private readonly MemoryInputLimits limits = limitsOptions.Value;

    public async Task<PublicMemoryUploadSessionResult> CreateAsync(string publicCode, SubmitPublicMemoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var name, out var text, out var emoji, out var invalid)) return invalid!;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicMemoryUploadOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return new(PublicMemoryUploadOutcome.NotFound);
        var grant = await ResolveEntitlementsAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (grant is null || !MemoryProjectionPolicy.CanAcceptSubmission(config?.IsEnabled ?? false, grant.MemoriesEnabled,
                invitationEffectivelyActive: true) || !GuestMediaAvailable(grant))
            return new(PublicMemoryUploadOutcome.NotFound);
        if (!mediaUploadAvailability.IsAvailable)
            return new(PublicMemoryUploadOutcome.UploadUnavailable);

        if (!submissionLimiter.TryAcquire(access.InvitationId)) return new(PublicMemoryUploadOutcome.RateLimited);

        var count = await dbContext.Memories.CountAsync(memory =>
            memory.InvitationId == access.InvitationId && memory.State != MemoryState.Abandoned, cancellationToken);
        if (count >= limits.MaxMemoriesPerInvitation)
            return new(PublicMemoryUploadOutcome.Conflict, Code: "memory_quota_reached");

        var memory = Memory.Create(Guid.NewGuid(), access.InvitationId, name, text, emoji, expectsMedia: true, now);
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.AddMinutes(limits.UploadCapabilityLifetimeMinutes);
        var capability = MemoryUploadCapability.Create(Guid.NewGuid(), memory.Id, MemoryUploadCapability.RequiredPurpose,
            version, ComputeDigest(key, memory.Id, token), now, expiresAt);
        dbContext.Memories.Add(memory);
        dbContext.MemoryUploadCapabilities.Add(capability);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicMemoryUploadOutcome.Ok,
            new PublicMemoryUploadSession(memory.Id, TruncateToMinute(now), expiresAt, ToLimits(grant)), token);
    }

    public async Task<PublicMemoryUploadIntentResult> CreateIntentAsync(string publicCode, Guid memoryId,
        string? capabilityToken, CreatePublicMemoryMediaIntentRequest request, Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryValidateIntent(request, idempotencyKey, out var kind, out var duration, out var invalid)) return invalid!;
        if (memoryId == Guid.Empty || !TryDecodeToken(capabilityToken)) return new(PublicMemoryUploadOutcome.NotFound);

        GuestMediaReservation reservation;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var context = await AuthorizeAsync(publicCode, memoryId, capabilityToken!, lockInvitation: true, cancellationToken);
            if (context is null) return new(PublicMemoryUploadOutcome.NotFound);
            if (!mediaUploadAvailability.IsAvailable) return new(PublicMemoryUploadOutcome.UploadUnavailable);
            // Consulted only after the capability proved valid, so it cannot be used to probe memories or invitations.
            if (!intentLimiter.TryAcquire(context.Access.InvitationId)) return new(PublicMemoryUploadOutcome.RateLimited);

            var outcome = await guestMedia.ReserveAsync(new GuestMediaReservationCommand(
                context.Access.AccountId, context.Access.GrantId, context.Access.InvitationId, memoryId, idempotencyKey,
                kind, request.DeclaredByteLength, duration, context.Capability.ExpiresAt.ToUniversalTime(),
                context.Memory.Media.Count, limits.MaxMediaPerMemory), cancellationToken);
            if (outcome.Reservation is null) return MapReservationFailure(outcome.Failure);
            reservation = outcome.Reservation;

            if (!reservation.Replayed)
            {
                var link = context.Memory.AttachMedia(reservation.AssetId, Guid.NewGuid());
                // The memory is tracked and the link carries a client-generated key: mark it Added so EF inserts it.
                dbContext.Entry(link).State = EntityState.Added;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // The provider call happens only after the reservation (quota) committed, never inside the invitation lock.
        var capability = await guestMedia.IssueCapabilityAsync(reservation, cancellationToken);
        if (capability is null) return new(PublicMemoryUploadOutcome.UploadUnavailable);
        return new(PublicMemoryUploadOutcome.Ok, new PublicMemoryUploadIntent(reservation.AssetId, kind.ToString(),
            reservation.ExpiresAt, reservation.Replayed, capability.IngressUri, capability.ExpiresAt,
            capability.IngressHeaders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }

    public async Task<PublicMemoryUploadFinalizeResult> FinalizeAsync(string publicCode, Guid memoryId,
        string? capabilityToken, CancellationToken cancellationToken)
    {
        if (memoryId == Guid.Empty || !TryDecodeToken(capabilityToken)) return new(PublicMemoryUploadOutcome.NotFound);

        // Phase 1: read-only authorization to learn which assets to verify.
        var preflight = await AuthorizeAsync(publicCode, memoryId, capabilityToken!, lockInvitation: false, cancellationToken);
        if (preflight is null) return new(PublicMemoryUploadOutcome.NotFound);
        var invitationId = preflight.Access.InvitationId;
        var assetIds = preflight.Memory.Media.Select(item => item.MediaAssetId).ToArray();

        // Phase 2: verify provider evidence outside any transaction/lock. Browser callbacks are never trusted; Ready
        // comes only from server-side inspection (images normalized by the Worker, videos inspected at Stream).
        foreach (var assetId in assetIds)
        {
            try
            {
                await guestVerification.VerifyAsync(invitationId, assetId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or NotSupportedException)
            {
                // Provider unreachable/unavailable: the asset simply stays pending and the guest can finalize again.
            }
        }
        dbContext.ChangeTracker.Clear();

        // Phase 3: re-authorize under the invitation lock and apply the memory transition atomically.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var context = await AuthorizeAsync(publicCode, memoryId, capabilityToken!, lockInvitation: true, cancellationToken);
        if (context is null) return new(PublicMemoryUploadOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        var currentIds = context.Memory.Media.Select(item => item.MediaAssetId).ToArray();
        var statuses = (await assetStatuses.ListAsync(invitationId, currentIds, cancellationToken))
            .ToDictionary(item => item.AssetId);
        if (statuses.Values.Any(item => item.Readiness == GuestMediaAssetReadiness.Pending))
        {
            // At least one asset is still uploading/processing: the capability stays valid so the guest can finalize again.
            return new(PublicMemoryUploadOutcome.Ok, PublicMemoryFinalizeState.Processing);
        }

        var rejected = currentIds.Where(id => !statuses.TryGetValue(id, out var status) ||
                                              status.Readiness != GuestMediaAssetReadiness.Ready).ToArray();
        foreach (var assetId in rejected) context.Memory.DropMedia(assetId);
        var accepted = currentIds.Length - rejected.Length;

        PublicMemoryFinalizeState state;
        if (context.Memory.Media.Count > 0 || context.Memory.Text is not null || context.Memory.Emoji is not null)
        {
            context.Memory.Finalize(now);
            context.Capability.Consume(now);
            state = PublicMemoryFinalizeState.Published;
        }
        else
        {
            // Nothing publishable remains (every upload was rejected and the guest wrote no text/emoji).
            context.Memory.Abandon();
            context.Capability.Revoke(now);
            state = PublicMemoryFinalizeState.Rejected;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicMemoryUploadOutcome.Ok, state, accepted, rejected.Length);
    }

    public async Task<PublicMemoryUploadStatusResult> GetStatusAsync(string publicCode, Guid memoryId,
        string? capabilityToken, CancellationToken cancellationToken)
    {
        if (memoryId == Guid.Empty || !TryDecodeToken(capabilityToken)) return new(PublicMemoryUploadOutcome.NotFound);
        var context = await AuthorizeAsync(publicCode, memoryId, capabilityToken!, lockInvitation: false, cancellationToken);
        if (context is null) return new(PublicMemoryUploadOutcome.NotFound);

        var ids = context.Memory.Media.OrderBy(item => item.Ordinal).Select(item => item.MediaAssetId).ToArray();
        var statuses = (await assetStatuses.ListAsync(context.Access.InvitationId, ids, cancellationToken))
            .ToDictionary(item => item.AssetId);
        var media = ids.Where(statuses.ContainsKey).Select(id => new PublicMemoryUploadMediaStatus(id,
            statuses[id].Kind.ToString(), statuses[id].Readiness switch
            {
                GuestMediaAssetReadiness.Pending => "processing",
                GuestMediaAssetReadiness.Ready => "ready",
                _ => "rejected"
            })).ToArray();
        return new(PublicMemoryUploadOutcome.Ok, new PublicMemoryUploadStatus("pendingMedia",
            context.Capability.ExpiresAt, media));
    }

    private sealed record AuthorizedUpload(InvitationMemoriesGuestAccess Access, MemoryUploadCapability Capability, Memory Memory);

    /// <summary>
    /// Single authorization path for every post-create operation: effective-Active invitation (never Scheduled), module
    /// enabled, entitlement on, capability unexpired/unconsumed/unrevoked and bound to this exact memory, memory in
    /// PendingMedia and owned by the invitation resolved from the public code. Any failure returns null.
    /// </summary>
    private async Task<AuthorizedUpload?> AuthorizeAsync(string publicCode, Guid memoryId, string token,
        bool lockInvitation, CancellationToken cancellationToken)
    {
        var access = lockInvitation
            ? await invitationAccess.LockAndReadAsync(publicCode, cancellationToken)
            : await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null) return null;
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return null;
        var grant = await ResolveEntitlementsAsync(access, cancellationToken);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (grant is null || !MemoryProjectionPolicy.CanAcceptSubmission(config?.IsEnabled ?? false, grant.MemoriesEnabled,
                invitationEffectivelyActive: true)) return null;

        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var capability = await dbContext.MemoryUploadCapabilities.SingleOrDefaultAsync(item =>
            item.MemoryId == memoryId && item.Purpose == MemoryUploadCapability.RequiredPurpose &&
            item.HmacKeyVersion == version && item.ConsumedAt == null && item.RevokedAt == null && item.ExpiresAt > now,
            cancellationToken);
        if (capability is null || !DigestMatches(capability.HmacDigest, key, memoryId, token)) return null;

        var memory = await dbContext.Memories.Include(item => item.Media).SingleOrDefaultAsync(item =>
            item.Id == memoryId && item.InvitationId == access.InvitationId && item.State == MemoryState.PendingMedia,
            cancellationToken);
        return memory is null ? null : new AuthorizedUpload(access, capability, memory);
    }

    private async Task<EffectiveEntitlementSnapshot?> ResolveEntitlementsAsync(InvitationMemoriesGuestAccess access,
        CancellationToken cancellationToken)
    {
        var result = await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.MemorySubmission), cancellationToken);
        return result.IsGranted ? result.Entitlements : null;
    }

    private Task<MemoryConfiguration?> LoadConfigurationAsync(Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.MemoryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);

    private static bool GuestMediaAvailable(EffectiveEntitlementSnapshot grant) =>
        (grant.MaxGuestImages > 0 && grant.MaxGuestImageSizeMb > 0) ||
        (grant.MaxGuestVideos > 0 && grant.MaxGuestVideoSizeMb > 0 && grant.MaxGuestVideoDurationSeconds > 0);

    private PublicMemoryUploadLimits ToLimits(EffectiveEntitlementSnapshot grant) => new(limits.MaxMediaPerMemory,
        grant.MaxGuestImages > 0 ? grant.MaxGuestImageSizeMb * BytesPerMegabyte : 0,
        grant.MaxGuestVideos > 0 ? grant.MaxGuestVideoSizeMb * BytesPerMegabyte : 0,
        grant.MaxGuestVideos > 0 ? grant.MaxGuestVideoDurationSeconds : 0);

    private static PublicMemoryUploadIntentResult MapReservationFailure(GuestMediaReservationFailure? failure) => failure switch
    {
        GuestMediaReservationFailure.InvalidGrant => new(PublicMemoryUploadOutcome.NotFound),
        GuestMediaReservationFailure.EntitlementLimit => new(PublicMemoryUploadOutcome.Invalid, Errors:
            new Dictionary<string, string[]> { ["declaredByteLength"] = ["The declared size or duration exceeds the allowed maximum."] }),
        GuestMediaReservationFailure.QuotaExceeded => new(PublicMemoryUploadOutcome.Conflict, Code: "media_quota_reached"),
        GuestMediaReservationFailure.OwnerLimit => new(PublicMemoryUploadOutcome.Conflict, Code: "memory_media_limit"),
        GuestMediaReservationFailure.IdempotencyConflict => new(PublicMemoryUploadOutcome.Conflict, Code: "idempotency_conflict"),
        _ => new(PublicMemoryUploadOutcome.Invalid, Errors:
            new Dictionary<string, string[]> { ["request"] = ["The media upload request is invalid."] })
    };

    private static bool TryValidateIntent(CreatePublicMemoryMediaIntentRequest? request, Guid idempotencyKey,
        out GuestMediaKind kind, out long duration, out PublicMemoryUploadIntentResult? invalid)
    {
        kind = GuestMediaKind.Image;
        duration = 0;
        invalid = null;
        string? error = null;
        if (request is null) error = "A request body is required.";
        else if (idempotencyKey == Guid.Empty) error = "A non-empty GUID Idempotency-Key header is required.";
        else if (request.Kind is not ("Image" or "Video")) error = "Kind must be 'Image' or 'Video'.";
        else if (request.DeclaredByteLength <= 0) error = "The declared byte length must be positive.";
        else
        {
            kind = request.Kind == "Video" ? GuestMediaKind.Video : GuestMediaKind.Image;
            duration = request.DeclaredDurationSeconds ?? 0;
            if (kind == GuestMediaKind.Video && duration <= 0) error = "A positive declared duration is required for video.";
            else if (kind == GuestMediaKind.Image && duration != 0) error = "A declared duration is only allowed for video.";
        }

        if (error is null) return true;
        invalid = new(PublicMemoryUploadOutcome.Invalid, Errors: new Dictionary<string, string[]> { ["request"] = [error] });
        return false;
    }

    private bool TryValidate(SubmitPublicMemoryRequest request, out string? name, out string? text, out string? emoji,
        out PublicMemoryUploadSessionResult? invalid)
    {
        name = text = emoji = null;
        invalid = null;
        if (!MemoryTextPolicy.TryNormalize(request.DisplayName, MemoryTextPolicy.Field.DisplayName, out name, out var error))
            return Fail("displayName", error!, out invalid);
        if (!MemoryTextPolicy.TryNormalize(request.Text, MemoryTextPolicy.Field.Text, out text, out error))
            return Fail("text", error!, out invalid);
        if (!MemoryTextPolicy.TryNormalize(request.Emoji, MemoryTextPolicy.Field.Emoji, out emoji, out error))
            return Fail("emoji", error!, out invalid);
        var message = limits.ValidateSubmission(name, text, emoji, mediaCount: 1);
        if (message is null) return true;
        var key = name is not null && name.Length > limits.MaxDisplayNameCharacters ? "displayName"
            : text is not null && text.Length > limits.MaxTextCharacters ? "text"
            : emoji is not null && emoji.Length > limits.MaxEmojiCharacters ? "emoji" : "memory";
        return Fail(key, message, out invalid);
    }

    private static bool Fail(string key, string message, out PublicMemoryUploadSessionResult? invalid)
    {
        invalid = new(PublicMemoryUploadOutcome.Invalid, Errors: new Dictionary<string, string[]> { [key] = [message] });
        return false;
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    private static bool TryDecodeToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            return bytes.Length == 32 && string.Equals(WebEncoders.Base64UrlEncode(bytes), token, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static (byte[] Key, int Version) ReadSigningKey(MemoryUploadCapabilityOptions options)
    {
        if (options.HmacKeyVersion <= 0 || string.IsNullOrWhiteSpace(options.HmacKeyBase64))
            throw new InvalidOperationException("MemoryUploadCapabilities:HmacKeyBase64 and a positive HmacKeyVersion are required.");
        byte[] key;
        try { key = Convert.FromBase64String(options.HmacKeyBase64); }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("MemoryUploadCapabilities:HmacKeyBase64 must be valid base64.", exception);
        }
        if (key.Length < 32) throw new InvalidOperationException("Memory upload capability HMAC key must contain at least 256 bits.");
        return (key, options.HmacKeyVersion);
    }

    internal static byte[] ComputeDigest(byte[] key, Guid memoryId, string token) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{MemoryUploadCapability.RequiredPurpose}\n{memoryId:D}\n{token}"));

    private static bool DigestMatches(byte[] storedDigest, byte[] key, Guid memoryId, string token) =>
        CryptographicOperations.FixedTimeEquals(storedDigest, ComputeDigest(key, memoryId, token));
}
