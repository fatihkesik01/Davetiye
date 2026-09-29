namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

// Request/result DTOs for IAuthAccountService, grouped in one file because each is a small,
// framework-free record tightly coupled to a single use case on that interface. Application has no
// package/framework references (Davetiye.ArchitectureTests enforces this), so every member here is
// limited to plain BCL types.

public sealed record RegisterAccountRequest(string Email, string Password, string DisplayName);

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
    LockedOut,
    Banned,
    EmailNotConfirmed,

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
