using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Media;

public sealed class CreatorMediaIntentService(
    ICreatorMediaIntentStore store,
    IAccountReferenceValidator accountValidator,
    ICreatorMediaInvitationAccessReader invitationAccessReader,
    ITemplateSelectionResolver templateSelectionResolver,
    IEffectiveEntitlementResolver entitlementResolver,
    IOutermostAccountQuotaTransactionRunner transactionRunner,
    ICreatorMediaIntentRateLimiter rateLimiter,
    IMediaUploadGateway videoUploads,
    IMediaImageNormalizationPipeline imageUploads,
    IClock clock) : ICreatorMediaIntentService
{
    private static readonly TimeSpan IntentLifetime = TimeSpan.FromMinutes(15);

    public async Task<CreatorMediaIntentOutcome> CreateAsync(
        CreateCreatorMediaIntentCommand command,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.AccountId == Guid.Empty || command.InvitationId == Guid.Empty ||
            command.IdempotencyKey == Guid.Empty || !Enum.IsDefined(command.Kind) ||
            !Enum.IsDefined(command.PresentationRole) || command.DeclaredByteLength <= 0)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(clientIpAddress) || !rateLimiter.TryAcquire(command.AccountId, clientIpAddress))
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.RateLimited);
        }

        // The runner guarantees that this returns only after the outermost reservation transaction
        // has committed. Provider side effects must never happen inside that transaction/savepoint.
        var reservation = await transactionRunner.ExecuteAndCommitAsync(command.AccountId,
            token => CreateInTransactionAsync(command, token), cancellationToken);
        if (reservation.Failure is not null || reservation.Result is null)
        {
            return reservation;
        }

        var capability = await CreateCapabilityAsync(
            command, reservation.Result.AssetId, reservation.Result.ExpiresAt,
            command.DeclaredByteLength, reservation.MaximumDurationSeconds, cancellationToken);
        return capability is null
            ? CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.UploadUnavailable)
            : CreatorMediaIntentOutcome.Created(reservation.Result, capability);
    }

    private async Task<CreatorMediaIntentOutcome> CreateInTransactionAsync(
        CreateCreatorMediaIntentCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var fingerprint = Fingerprint(command);

        if (await accountValidator.GetStatusAsync(command.AccountId, cancellationToken) != AccountReferenceStatus.Verified)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.AccountInactive);
        }

        // Owner-filtered lookup comes before idempotency lookup so a foreign invitation remains
        // indistinguishable from a missing one, even when a caller replays a key obtained elsewhere.
        var invitation = await invitationAccessReader.LoadAsync(command.AccountId, command.InvitationId, now, cancellationToken);
        if (invitation is null)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.NotFound);
        }

        // Expired, unconsumed intents stop reserving Creator quota before replay or usage is read.
        // The account transaction serializes this with every competing upload-intent request.
        await store.ExpireOpenReservationsAsync(command.InvitationId, now, cancellationToken);

        if (!invitation.CanIssueCreatorMediaIntent || invitation.CurrentGrantId is null)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.IneligibleInvitation);
        }

        var entitlementResult = await entitlementResolver.ResolveAsync(new EntitlementResolutionContext(
            command.AccountId, invitation.CurrentGrantId.Value, command.InvitationId,
            PublicationEntitlementAction.CreatorMediaUpload), cancellationToken);
        if (!entitlementResult.IsGranted || entitlementResult.Entitlements is null)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.InvalidGrant);
        }

        if (string.IsNullOrWhiteSpace(invitation.TemplateKey))
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.UnsupportedModule);
        }

        var template = await templateSelectionResolver.ResolveActiveAsync(invitation.TemplateKey, cancellationToken);
        if (!TemplateSupports(template?.SupportedModules, command.PresentationRole))
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.UnsupportedModule);
        }

        var grant = entitlementResult.Entitlements;
        var maximumBytes = MaximumBytes(command.Kind, grant);
        if (maximumBytes is null)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.InvalidGrant);
        }

        if (command.Kind == MediaKind.Video && grant.MaxVideoDurationSeconds <= 0)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.InvalidGrant);
        }

        if (command.DeclaredByteLength > maximumBytes.Value)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.EntitlementLimit);
        }

        var replay = await store.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (replay.InvitationId != command.InvitationId ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(replay.RequestFingerprint), Encoding.ASCII.GetBytes(fingerprint)) ||
                replay.AssetState != MediaAssetState.PendingUpload || replay.ConsumedAt is not null ||
                replay.CancelledAt is not null || replay.ExpiresAt <= now ||
                replay.Kind != command.Kind || replay.PresentationRole != command.PresentationRole ||
                replay.DeclaredByteLength != command.DeclaredByteLength)
            {
                return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.IdempotencyConflict);
            }

            // The provider port guarantees stable, single-resource capability issuance for this
            // AssetId. Retry only after current account/invitation/grant/template checks above.
            return CreatorMediaIntentOutcome.Reserved(
                new CreatorMediaIntentResult(replay.IntentId, replay.AssetId, replay.ExpiresAt, Replayed: true),
                command.Kind == MediaKind.Video ? entitlementResult.Entitlements.MaxVideoDurationSeconds : 0);
        }

        var usage = await store.GetCreatorUsageAsync(command.InvitationId, cancellationToken);
        var used = command.Kind == MediaKind.Image ? usage.Images : usage.Videos;
        var limit = command.Kind == MediaKind.Image ? grant.MaxImages : grant.MaxVideos;
        if (used >= limit)
        {
            return CreatorMediaIntentOutcome.Rejected(CreatorMediaIntentFailure.QuotaExceeded);
        }

        var assetId = Guid.NewGuid();
        var asset = MediaAsset.CreateCreatorAsset(assetId, command.InvitationId, command.Kind, now);
        var expiresAt = now.Add(IntentLifetime);
        var intent = PendingUpload.Create(
            Guid.NewGuid(), assetId, command.IdempotencyKey, command.PresentationRole,
            command.DeclaredByteLength, fingerprint, now, expiresAt, maximumBytes.Value,
            command.Kind == MediaKind.Video ? grant.MaxVideoDurationSeconds : 0);

        // Persist the quota reservation before returning from the transaction. Capability issuance
        // occurs only after ExecuteAndCommitAsync confirms the outermost database commit.
        await store.SaveIntentAsync(asset, intent, cancellationToken);

        return CreatorMediaIntentOutcome.Reserved(new CreatorMediaIntentResult(
            intent.Id, asset.Id, expiresAt, Replayed: false),
            command.Kind == MediaKind.Video ? entitlementResult.Entitlements.MaxVideoDurationSeconds : 0);
    }

    private async Task<MediaUploadCapability?> CreateCapabilityAsync(
        CreateCreatorMediaIntentCommand command, Guid assetId, DateTimeOffset expiresAt, long maximumBytes,
        long maximumDurationSeconds,
        CancellationToken cancellationToken)
    {
        var request = new MediaUploadRequest(assetId, command.Kind, MediaQuotaScope.Creator, maximumBytes, expiresAt,
            maximumDurationSeconds);
        try
        {
            var capability = command.Kind == MediaKind.Image
                ? await imageUploads.CreateNormalizedImageIngressAsync(request, cancellationToken)
                : await videoUploads.CreateVideoCapabilityAsync(request, cancellationToken);
            if (capability is null || !capability.IngressUri.IsAbsoluteUri ||
                capability.IngressUri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(capability.IngressUri.UserInfo) || !string.IsNullOrEmpty(capability.IngressUri.Fragment) ||
                !ValidIngressHeaders(capability.IngressHeaders, command.Kind) ||
                capability.ExpiresAt.Offset != TimeSpan.Zero || capability.ExpiresAt <= clock.UtcNow || capability.ExpiresAt > expiresAt)
            {
                return null;
            }

            return capability;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static long? MaximumBytes(MediaKind kind, EffectiveEntitlementSnapshot access)
    {
        var megabytes = kind == MediaKind.Image ? access.MaxImageSizeMb : access.MaxVideoSizeMb;
        if (megabytes <= 0 || megabytes > long.MaxValue / (1024 * 1024))
        {
            return null;
        }

        return megabytes * 1024 * 1024;
    }

    private static bool TemplateSupports(IReadOnlyCollection<string>? modules, MediaPresentationRole role)
    {
        var requiredModule = role == MediaPresentationRole.Cover ? "hero" : "gallery";
        return modules?.Contains(requiredModule, StringComparer.Ordinal) == true;
    }

    private static bool ValidIngressHeaders(IReadOnlyDictionary<string, string>? headers, MediaKind kind)
    {
        if (headers is not null && headers.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Any(char.IsControl) || pair.Value.Length > 4096))
        {
            return false;
        }

        return kind != MediaKind.Image ||
               headers?.Any(pair => string.Equals(pair.Key, "X-Media-Capability", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string Fingerprint(CreateCreatorMediaIntentCommand command)
    {
        var canonical = string.Join('|', command.AccountId.ToString("N"), command.InvitationId.ToString("N"),
            command.Kind.ToString(), command.PresentationRole.ToString(), command.DeclaredByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
