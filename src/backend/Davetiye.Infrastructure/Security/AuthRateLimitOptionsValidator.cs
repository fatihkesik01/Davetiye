using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

internal sealed class AuthRateLimitOptionsValidator : IValidateOptions<AuthRateLimitOptions>
{
    public const int MinPermitLimit = 1;
    public const int MaxPermitLimit = 1000;
    public const int MinWindowSeconds = 1;
    public const int MaxWindowSeconds = 3600;

    public ValidateOptionsResult Validate(string? name, AuthRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        Validate("Register", options.Register, failures);
        Validate("Login", options.Login, failures);
        Validate("PasswordResetRequest", options.PasswordResetRequest, failures);
        Validate("PasswordResetRequestDestination", options.PasswordResetRequestDestination, failures);
        if (options.PasswordResetDestinationBucketCount is < 1024 or > 1_048_576)
            failures.Add("AuthRateLimits:PasswordResetDestinationBucketCount must be between 1024 and 1048576.");
        Validate("EmailConfirmation", options.EmailConfirmation, failures);
        Validate("PasswordResetConfirm", options.PasswordResetConfirm, failures);
        Validate("AntiforgeryToken", options.AntiforgeryToken, failures);
        Validate("TwoFactorLoginComplete", options.TwoFactorLoginComplete, failures);
        Validate("AdminMfaVerify", options.AdminMfaVerify, failures);
        Validate("PublicationRead", options.PublicationRead, failures);
        Validate("PublicationAction", options.PublicationAction, failures);
        Validate("UiPreferencesWrite", options.UiPreferencesWrite, failures);
        Validate("PublicInvitationRead", options.PublicInvitationRead, failures);
        Validate("PublicRsvpSubmission", options.PublicRsvpSubmission, failures);
        Validate("CreatorMediaIntentIp", options.CreatorMediaIntentIp, failures);
        Validate("CreatorMediaIntentAccount", options.CreatorMediaIntentAccount, failures);
        Validate("CreatorRsvpReadIp", options.CreatorRsvpReadIp, failures);
        Validate("CreatorRsvpWriteIp", options.CreatorRsvpWriteIp, failures);
        Validate("CreatorRsvpReadAccount", options.CreatorRsvpReadAccount, failures);
        Validate("CreatorRsvpWriteAccount", options.CreatorRsvpWriteAccount, failures);
        Validate("PublicMemorySubmission", options.PublicMemorySubmission, failures);
        Validate("PublicMemoryMediaDelivery", options.PublicMemoryMediaDelivery, failures);
        Validate("PublicMemorySubmissionPerInvitation", options.PublicMemorySubmissionPerInvitation, failures);
        Validate("PublicMemoryUploadCreate", options.PublicMemoryUploadCreate, failures);
        Validate("PublicMemoryUploadIntent", options.PublicMemoryUploadIntent, failures);
        Validate("PublicMemoryUploadFinalize", options.PublicMemoryUploadFinalize, failures);
        Validate("PublicMemoryUploadIntentPerInvitation", options.PublicMemoryUploadIntentPerInvitation, failures);
        Validate("CreatorMemoriesReadIp", options.CreatorMemoriesReadIp, failures);
        Validate("CreatorMemoriesWriteIp", options.CreatorMemoriesWriteIp, failures);
        Validate("CreatorMemoriesReadAccount", options.CreatorMemoriesReadAccount, failures);
        Validate("CreatorMemoriesWriteAccount", options.CreatorMemoriesWriteAccount, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Validate(
        string routeClassName,
        AuthRateLimitOptions.RouteRateLimit routeLimit,
        ICollection<string> failures)
    {
        if (routeLimit.PermitLimit is < MinPermitLimit or > MaxPermitLimit)
        {
            failures.Add(
                $"AuthRateLimits:{routeClassName}:PermitLimit must be between {MinPermitLimit} and {MaxPermitLimit}.");
        }

        if (routeLimit.WindowSeconds is < MinWindowSeconds or > MaxWindowSeconds)
        {
            failures.Add(
                $"AuthRateLimits:{routeClassName}:WindowSeconds must be between {MinWindowSeconds} and {MaxWindowSeconds}.");
        }
    }
}
