namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Configurable password/lockout policy fed into ASP.NET Core Identity's own
/// <see cref="Microsoft.AspNetCore.Identity.IdentityOptions"/> (Password/Lockout sections), rather
/// than hardcoding magic numbers directly where Identity is configured, per AGENTS.md's "never
/// hardcode configurable business limits" and the established
/// <see cref="Microsoft.Extensions.Options.IValidateOptions{TOptions}"/> + <c>ValidateOnStart()</c>
/// convention already used for <c>RequestLimitsOptions</c>/<c>TrustedProxyOptions</c>.
/// </summary>
public sealed class IdentityPolicyOptions
{
    public const string SectionName = "IdentityPolicy";

    public int PasswordRequiredLength { get; init; } = 10;

    public bool PasswordRequireDigit { get; init; } = true;

    public bool PasswordRequireUppercase { get; init; } = true;

    public bool PasswordRequireLowercase { get; init; } = true;

    public bool PasswordRequireNonAlphanumeric { get; init; }

    public int MaxFailedAccessAttempts { get; init; } = 5;

    public int LockoutDurationMinutes { get; init; } = 15;
}
