namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Configurable Super Admin TOTP/recovery-code MFA behavior (docs/PHASE_0_PLAN.md §7,
/// docs/adr/0002). Only the recovery-code batch size is configurable here; the authenticator
/// algorithm/validation window itself always uses ASP.NET Core Identity's own built-in TOTP token
/// provider (<c>TokenOptions.DefaultAuthenticatorProvider</c>, wired via
/// <c>AddDefaultTokenProviders()</c> in <see cref="SecurityServiceCollectionExtensions"/>) rather than
/// any custom crypto.
/// </summary>
public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    public int RecoveryCodeCount { get; init; } = 10;
}
