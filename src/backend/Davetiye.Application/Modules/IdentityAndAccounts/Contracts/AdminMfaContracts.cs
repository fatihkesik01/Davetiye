namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

// Request/result DTOs for IAdminMfaService, grouped in one file following the same convention as
// AuthAccountContracts.cs.

public sealed record EnrollMfaResult(string SharedKey, string AuthenticatorUri);

public enum VerifyMfaOutcome
{
    Succeeded,
    InvalidCode,
    AlreadyEnabled,
    LockedOut,
}

public sealed record VerifyMfaResult(VerifyMfaOutcome Outcome, IReadOnlyCollection<string> RecoveryCodes);

public sealed record CompleteTwoFactorLoginRequest(string Code, bool IsRecoveryCode);

public enum CompleteTwoFactorLoginOutcome
{
    Succeeded,
    InvalidCode,
    LockedOut,
}

public sealed record CompleteTwoFactorLoginResult(CompleteTwoFactorLoginOutcome Outcome);
