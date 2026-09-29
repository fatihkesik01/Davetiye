namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

// Request/result DTOs for IGoogleSignInService, grouped in one file following the same convention as
// AuthAccountContracts.cs.

public enum GoogleSignInOutcome
{
    Succeeded,

    /// <summary>
    /// The callback was reached without a valid external-login context (e.g. a direct hit on the
    /// completion endpoint, an expired correlation cookie, or a cancelled consent screen).
    /// </summary>
    ExternalLoginInfoMissing,

    /// <summary>
    /// Google's email matches an existing email/password account that has not linked a Google login.
    /// docs/THREAT_MODEL.md §6: "Unverified veya yalnız eşleşen email ile hesap merge edilmez" — no
    /// silent account merge happens here. The exact linking UX is an unspecified product decision
    /// (not in docs/PRODUCT.md or any accepted ADR), so this foundation milestone surfaces a
    /// distinguishable rejection instead of inventing one.
    /// </summary>
    EmailAlreadyRegisteredWithoutLinking,

    /// <summary>
    /// Google did not report this identity's email as verified (missing or <c>false</c>
    /// "email_verified" claim). Rejected fail-closed before the email is used for anything — matching
    /// email/password registration's <c>RequireConfirmedAccount</c> mitigation for the same
    /// email-squatting risk (docs/THREAT_MODEL.md §6).
    /// </summary>
    EmailNotVerifiedByProvider,

    /// <summary>
    /// The Google identity is already linked to an Account with an active (unrevoked) <c>BanRecord</c>.
    /// Mirrors <c>AuthAccountService.LoginAsync</c>'s ban check for the email/password path
    /// (docs/THREAT_MODEL.md T11) — the caller must never receive a signed-in result for a banned
    /// account, including through the Google fast path.
    /// </summary>
    Banned,

    Failed,
}

/// <summary>
/// <paramref name="RedirectUrl"/> is only populated on <see cref="GoogleSignInOutcome.Succeeded"/> and
/// is already resolved against the local-only return-url policy (docs/THREAT_MODEL.md §6: "returnUrl
/// yalnız local allowlist içinden seçilir; open redirect yoktur").
/// </summary>
public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, string? RedirectUrl);
