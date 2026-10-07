namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

// Request/result DTOs for IAuthAccountService, grouped in one file because each is a small,
// framework-free record tightly coupled to a single use case on that interface. Application has no
// package/framework references (Davetiye.ArchitectureTests enforces this), so every member here is
// limited to plain BCL types.

/// <summary>
/// <paramref name="AccountType"/> is the caller's explicit "Individual" or "Organization" choice
/// (docs/PRODUCT.md §2-3, the 2026-09-29 product decision recorded in docs/ROADMAP.md §4a: chosen
/// explicitly at registration, never inferred, and never convertible afterward — see
/// <see cref="Davetiye.Domain.Modules.IdentityAndAccounts.Account"/>, whose <c>AccountType</c>
/// property has no setter beyond <c>Create</c>). Kept as a plain string here (parsed and validated
/// against <c>Davetiye.Domain.Modules.IdentityAndAccounts.AccountType</c> in
/// <c>AuthAccountService.RegisterAsync</c>) rather than referencing the Domain enum type directly, so
/// this contract file's shape stays obviously plain-BCL/self-describing for API consumers.
/// </summary>
public sealed record RegisterAccountRequest(
    string Email,
    string Password,
    string DisplayName,
    string AccountType,
    bool ServiceNoticeAcknowledged,
    bool MarketingOptIn = false);

public enum RegisterAccountOutcome
{
    Succeeded,
    EmailAlreadyRegistered,
    InvalidPassword,
    InvalidRequest,
}

public sealed record RegisterAccountResult(RegisterAccountOutcome Outcome, IReadOnlyCollection<string> Errors)
{
    public static RegisterAccountResult Succeeded() =>
        new(RegisterAccountOutcome.Succeeded, []);
}

public sealed record ConfirmEmailRequest(string UserId, string Token);

public enum ConfirmEmailOutcome
{
    Succeeded,
    InvalidRequest,
}

public sealed record ConfirmEmailResult(ConfirmEmailOutcome Outcome);

public sealed record LoginRequest(string Email, string Password);

public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    Banned,

    /// <summary>
    /// The password check succeeded but the account has two-factor authentication enabled
    /// (M6b/docs/adr/0002 — currently only ever true for the Super Admin principal). Sign-in is not
    /// yet complete: the caller must submit a valid TOTP/recovery code to
    /// <c>IAdminMfaService.CompleteTwoFactorLoginAsync</c> before a session cookie carrying the
    /// "MfaComplete" policy's claims exists.
    /// </summary>
    RequiresTwoFactor,
}

public sealed record LoginResult(LoginOutcome Outcome);

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(string UserId, string Token, string NewPassword);

public enum ResetPasswordOutcome
{
    Succeeded,
    InvalidRequest,
    InvalidPassword,
}

public sealed record ResetPasswordResult(ResetPasswordOutcome Outcome, IReadOnlyCollection<string> Errors);
