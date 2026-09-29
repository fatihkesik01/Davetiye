namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Coordinates the Identity & Accounts module's email/password auth use cases across the two EF
/// entities a signup/login touches (the ASP.NET Core Identity user and the Domain <c>Account</c>),
/// per M6a's scope: email/password register/confirm/login/logout/reset only (no Google, no Admin
/// bootstrap, no MFA — those are M6b). This is Application's narrow contract surface; the real
/// implementation depends on <c>UserManager</c>/<c>SignInManager</c>, which are Infrastructure
/// concerns, so it is implemented in Davetiye.Infrastructure.Modules.IdentityAndAccounts and
/// injected here as a plain interface (the same "thin Infrastructure-implemented port" shape
/// IntegrationFoundation already established for IInboxMessageRepository/IOutboxMessageRepository).
/// </summary>
public interface IAuthAccountService
{
    Task<RegisterAccountResult> RegisterAsync(RegisterAccountRequest request, CancellationToken cancellationToken);

    Task<ConfirmEmailResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Always completes the same way regardless of whether <paramref name="request"/>'s email
    /// exists, by design (docs/THREAT_MODEL.md §9: "Verify/reset endpoint'leri account enumeration
    /// yapmayan cevap verir") — there is deliberately no result/outcome to branch a caller's HTTP
    /// response on.
    /// </summary>
    Task RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken);

    Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
