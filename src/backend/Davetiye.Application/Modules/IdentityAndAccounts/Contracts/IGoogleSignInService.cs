namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

/// <summary>
/// Completes a Google OAuth sign-in after the framework's authentication middleware has already
/// exchanged the authorization code and established the external-login cookie
/// (<c>IdentityConstants.ExternalScheme</c>). This is the Identity &amp; Accounts module's Google-
/// specific parallel to <see cref="IAuthAccountService"/>'s email/password flow: a first-time sign-in
/// with a genuinely new email creates a new Identity user + <c>Account</c> transactionally, mirroring
/// <c>AuthAccountService.RegisterAsync</c>'s pattern using the explicit Individual/Organization
/// choice protected in the OAuth authentication properties. The real implementation depends on
/// <c>SignInManager</c>/<c>UserManager</c>,
/// which are Infrastructure concerns, so it is implemented in
/// Davetiye.Infrastructure.Modules.IdentityAndAccounts and injected here as a plain interface.
/// </summary>
public interface IGoogleSignInService
{
    /// <summary>
    /// The account type and safe local return path are read from the data-protected external-login
    /// authentication properties established by <see cref="BuildChallengePropertyItems"/>. Callback
    /// query parameters are never trusted for either value.
    /// </summary>
    Task<GoogleSignInResult> CompleteSignInAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes the second, explicit step of the M1 account-linking flow
    /// (docs/ROADMAP.md §4a's 2026-09-29 Google same-email-conflict decision):
    /// attaches the pending Google external login (read from the same
    /// <c>IdentityConstants.ExternalScheme</c> cookie <see cref="CompleteSignInAsync"/> reads, still
    /// present after a <see cref="GoogleSignInOutcome.AccountLinkRequired"/> result) to
    /// <paramref name="identityUserId"/>. In addition to an authenticated session, the supplied
    /// <paramref name="currentPassword"/> must verify immediately before the link is attached.
    /// Never a silent merge: this is the one explicit call site that actually links the two
    /// identities.
    /// </summary>
    Task<GoogleAccountLinkResult> ConfirmLinkAsync(
        Guid identityUserId,
        string currentPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the well-known property items ASP.NET Core Identity's own external-login flow needs
    /// on the <c>AuthenticationProperties</c> passed to the initial OAuth challenge, so that
    /// <c>SignInManager.GetExternalLoginInfoAsync</c> can later recognize the completed sign-in.
    /// Returned as a plain dictionary rather than the framework's own <c>AuthenticationProperties</c>
    /// type, since Application has no ASP.NET Core package reference — the Api layer merges these
    /// into its own <c>AuthenticationProperties</c> before calling <c>Results.Challenge</c>, rather
    /// than needing to know (or hardcode) Identity's internal property-item key itself.
    /// </summary>
    /// <param name="accountType">Required explicit Individual/Organization selection.</param>
    /// <param name="returnUrl">Optional caller-supplied app-local return path.</param>
    /// <returns>
    /// Data-protected OAuth property items, or <see langword="null"/> when account type is invalid.
    /// The returned account type is canonicalized and the return path is sanitized before either is
    /// placed into the authentication state.
    /// </returns>
    IReadOnlyDictionary<string, string?>? BuildChallengePropertyItems(
        string authenticationScheme,
        string? accountType,
        string? returnUrl,
        bool serviceNoticeAcknowledged,
        bool marketingOptIn);
}
