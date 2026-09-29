namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Completes a Google OAuth sign-in after the framework's authentication middleware has already
/// exchanged the authorization code and established the external-login cookie
/// (<c>IdentityConstants.ExternalScheme</c>). This is the Identity &amp; Accounts module's Google-
/// specific parallel to <see cref="IAuthAccountService"/>'s email/password flow: a first-time sign-in
/// with a genuinely new email creates a new Identity user + <c>Account</c> transactionally, mirroring
/// <c>AuthAccountService.RegisterAsync</c>'s pattern (Individual accounts only, per
/// docs/PRODUCT.md §3). The real implementation depends on <c>SignInManager</c>/<c>UserManager</c>,
/// which are Infrastructure concerns, so it is implemented in
/// Davetiye.Infrastructure.Modules.IdentityAndAccounts and injected here as a plain interface.
/// </summary>
public interface IGoogleSignInService
{
    /// <summary>
    /// <paramref name="returnUrl"/> is the caller-supplied post-login destination (validated against
    /// a local-only allowlist before being echoed back in a successful result — never trusted as an
    /// absolute redirect target).
    /// </summary>
    Task<GoogleSignInResult> CompleteSignInAsync(string? returnUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the well-known property items ASP.NET Core Identity's own external-login flow needs
    /// on the <c>AuthenticationProperties</c> passed to the initial OAuth challenge, so that
    /// <c>SignInManager.GetExternalLoginInfoAsync</c> can later recognize the completed sign-in.
    /// Returned as a plain dictionary rather than the framework's own <c>AuthenticationProperties</c>
    /// type, since Application has no ASP.NET Core package reference — the Api layer merges these
    /// into its own <c>AuthenticationProperties</c> before calling <c>Results.Challenge</c>, rather
    /// than needing to know (or hardcode) Identity's internal property-item key itself.
    /// </summary>
    IReadOnlyDictionary<string, string?> BuildChallengePropertyItems(string authenticationScheme);
}
