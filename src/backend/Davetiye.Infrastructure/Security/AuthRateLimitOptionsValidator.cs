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
        Validate("EmailConfirmation", options.EmailConfirmation, failures);
        Validate("PasswordResetConfirm", options.PasswordResetConfirm, failures);
        Validate("AntiforgeryToken", options.AntiforgeryToken, failures);
        Validate("TwoFactorLoginComplete", options.TwoFactorLoginComplete, failures);
        Validate("AdminMfaVerify", options.AdminMfaVerify, failures);

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
