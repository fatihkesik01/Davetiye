namespace Davetiye.Infrastructure.Security;

/// <summary>
/// The rate-limiter policy names registered by <c>AddInfrastructure</c>. Davetiye.Api's
/// composition root (<c>Program.cs</c>) reads these constants and passes the plain string values
/// down into the endpoint-mapping extension method, so that non-<c>Program.cs</c> Api files never
/// need to reference Davetiye.Infrastructure directly (see
/// Davetiye.ArchitectureTests' "Api uses Infrastructure only from the composition root" rule).
/// </summary>
public static class AuthRateLimitPolicyNames
{
    public const string Register = "auth-register";

    public const string Login = "auth-login";

    public const string PasswordResetRequest = "auth-password-reset-request";

    public const string EmailConfirmation = "auth-email-confirmation";

    public const string PasswordResetConfirm = "auth-password-reset-confirm";

    public const string AntiforgeryToken = "auth-antiforgery-token";

    public const string TwoFactorLoginComplete = "auth-two-factor-login-complete";

    public const string AdminMfaVerify = "auth-admin-mfa-verify";
    public const string PublicationRead = "creator-publication-read";
    public const string PublicationAction = "creator-publication-action";
    public const string PublicInvitationRead = "public-invitation-read";
    public const string PublicRsvpSubmission = "public-rsvp-submission";
    public const string CreatorMediaIntentIp = "creator-media-intent-ip";
    public const string CreatorRsvpReadIp = "creator-rsvp-read-ip";
    public const string CreatorRsvpWriteIp = "creator-rsvp-write-ip";
    public const string PublicMemorySubmission = "public-memory-submission";
    public const string PublicMemoryMediaDelivery = "public-memory-media-delivery";
    public const string PublicMemoryUploadCreate = "public-memory-upload-create";
    public const string PublicMemoryUploadIntent = "public-memory-upload-intent";
    public const string PublicMemoryUploadFinalize = "public-memory-upload-finalize";
    public const string CreatorMemoriesReadIp = "creator-memories-read-ip";
    public const string CreatorMemoriesWriteIp = "creator-memories-write-ip";
}
