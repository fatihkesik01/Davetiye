namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Route-class rate limit policy configuration (docs/THREAT_MODEL.md T14 password-reset
/// enumeration/brute force, T16 bot spam). Password-reset requests use both per-IP and
/// per-normalized-destination limits; other route classes retain their applicable dimensions.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimits";

    public RouteRateLimit Register { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit Login { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit PasswordResetRequest { get; init; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public RouteRateLimit PasswordResetRequestDestination { get; init; } = new() { PermitLimit = 5, WindowSeconds = 3600 };

    /// <summary>Fixed process-local destination bucket array size; changes take effect on restart.</summary>
    public int PasswordResetDestinationBucketCount { get; init; } = 262_144;

    public RouteRateLimit EmailConfirmation { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit PasswordResetConfirm { get; init; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    public RouteRateLimit AntiforgeryToken { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>
    /// Guards the TOTP/recovery-code two-factor login-completion endpoint. A 6-digit authenticator
    /// code has only 10^6 possibilities; combined with the account-lockout counter
    /// (<see cref="DavetiyeSignInManager.TwoFactorSignInWithAmrClaimAsync"/> already calls
    /// <c>UserManager.AccessFailedAsync</c> on a wrong code), this keeps a brute-force attempt slow
    /// even before lockout kicks in.
    /// </summary>
    public RouteRateLimit TwoFactorLoginComplete { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>
    /// Guards the TOTP verify-and-enable endpoint (enrollment confirmation). Same brute-force shape
    /// as <see cref="TwoFactorLoginComplete"/> (a 6-digit code) but reached with only a first-factor
    /// Super Admin session instead of a completed login. Account-level lockout accounting
    /// (<c>AdminMfaService.VerifyAndEnableAsync</c>) backs this up; this is the per-IP route-class
    /// layer, matching <see cref="TwoFactorLoginComplete"/>'s reasoning.
    /// </summary>
    public RouteRateLimit AdminMfaVerify { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RouteRateLimit PublicationRead { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };
    public RouteRateLimit PublicationAction { get; init; } = new() { PermitLimit = 20, WindowSeconds = 60 };
    public RouteRateLimit PublicInvitationRead { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>Anonymous RSVP writes: abuse-control default, configurable with the other route classes.</summary>
    public RouteRateLimit PublicRsvpSubmission { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit CreatorMediaIntentIp { get; init; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    public RouteRateLimit CreatorMediaIntentAccount { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RouteRateLimit CreatorRsvpReadIp { get; init; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    public RouteRateLimit CreatorRsvpWriteIp { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public RouteRateLimit CreatorRsvpReadAccount { get; init; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    public RouteRateLimit CreatorRsvpWriteAccount { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Anonymous Memory writes: engineering abuse-control default (not product-approved), configurable like the other route classes.</summary>
    public RouteRateLimit PublicMemorySubmission { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Anonymous Memories media delivery exchanges; configurable abuse-control default.</summary>
    public RouteRateLimit PublicMemoryMediaDelivery { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Per-invitation anonymous Memory writes, applied after the gate: engineering default (not product-approved), configurable up to the shared hard ceiling.</summary>
    public RouteRateLimit PublicMemorySubmissionPerInvitation { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>Anonymous guest memory-with-media creation: engineering abuse-control default (not product-approved).</summary>
    public RouteRateLimit PublicMemoryUploadCreate { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Anonymous guest upload-intent requests per IP: engineering default (not product-approved); each mints a provider capability.</summary>
    public RouteRateLimit PublicMemoryUploadIntent { get; init; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    /// <summary>Anonymous guest memory finalize per IP: engineering default (not product-approved); each may trigger provider inspection.</summary>
    public RouteRateLimit PublicMemoryUploadFinalize { get; init; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    /// <summary>Per-invitation guest upload intents, applied after the capability validated: engineering default (not product-approved).</summary>
    public RouteRateLimit PublicMemoryUploadIntentPerInvitation { get; init; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    public RouteRateLimit CreatorMemoriesReadIp { get; init; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    public RouteRateLimit CreatorMemoriesWriteIp { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public RouteRateLimit CreatorMemoriesReadAccount { get; init; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    public RouteRateLimit CreatorMemoriesWriteAccount { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    public sealed class RouteRateLimit
    {
        public int PermitLimit { get; init; }

        public int WindowSeconds { get; init; }
    }
}
