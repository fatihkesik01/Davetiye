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
    /// silent account merge happens here. Per the 2026-09-29 product decision (docs/ROADMAP.md §4a),
    /// the caller must log in with their existing password (proving ownership) and then call
    /// <see cref="IGoogleSignInService.ConfirmLinkAsync"/> to explicitly attach this Google identity —
    /// never a silent merge.
    /// </summary>
    AccountLinkRequired,

    /// <summary>
    /// The matched Identity user (either already linked to this Google identity, or found by
    /// matching email for a not-yet-linked account) carries the Super Admin claim.
    /// docs/adr/0002: a Super Admin principal must never authenticate or link via Google. Enforced in
    /// code here (not just assumed) as of the M1 account-linking feature — see
    /// <c>GoogleSignInService.CompleteSignInAsync</c>'s doc comment for why this used to be a
    /// code-unenforced assumption safe only because no linking flow existed yet.
    /// </summary>
    SuperAdminGoogleSignInNotAllowed,

    /// <summary>
    /// Google did not report this identity's email as verified (missing or <c>false</c>
    /// "email_verified" claim). Rejected fail-closed before the email is used for anything — matching
    /// email/password registration's <c>RequireConfirmedAccount</c> mitigation for the same
    /// email-squatting risk (docs/THREAT_MODEL.md §6).
    /// </summary>
    EmailNotVerifiedByProvider,

    /// <summary>A new Google account cannot be created without the required service notice acknowledgement.</summary>
    ServiceNoticeAcknowledgementRequired,

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
/// Request body for the explicit Google account-link confirmation. The current password is checked
/// server-side immediately before the external login is attached; an existing application cookie
/// alone is intentionally insufficient for this security-sensitive operation.
/// </summary>
public sealed record ConfirmGoogleAccountLinkRequest(string Password);

/// <summary>
/// <paramref name="RedirectUrl"/> is populated on <see cref="GoogleSignInOutcome.Succeeded"/> and
/// <see cref="GoogleSignInOutcome.AccountLinkRequired"/>. It is always built from the trusted public
/// web base URL plus either the sanitized local return path or the fixed account-linking route
/// (docs/THREAT_MODEL.md §6: "returnUrl yalnız local allowlist içinden seçilir; open redirect yoktur").
/// </summary>
public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, string? RedirectUrl);

/// <summary>
/// Outcomes for <see cref="IGoogleSignInService.ConfirmLinkAsync"/> — the second, explicit step of
/// the M1 account-linking flow. The caller must already hold an authenticated session for the
/// account being linked into before this is ever reached; this method also verifies the supplied
/// current password and re-validates that the pending Google identity's own verified email still
/// matches that authenticated account.
/// </summary>
public enum GoogleAccountLinkOutcome
{
    Succeeded,

    /// <summary>
    /// No pending Google external-login attempt was found on this browser (missing/expired
    /// External-scheme cookie, or this endpoint was called without first attempting Google sign-in).
    /// </summary>
    NoPendingExternalLogin,

    /// <summary>
    /// The pending Google identity's verified email does not match the currently authenticated
    /// account's email. This scopes linking to exactly the same-email collision this feature exists
    /// to resolve — it is not a general "link any Google account to any Creator account" feature.
    /// </summary>
    EmailMismatch,

    /// <summary>This Google identity (provider + <c>sub</c>) is already linked to a different account.</summary>
    AlreadyLinkedToAnotherAccount,

    /// <summary>
    /// The authenticated account carries the Super Admin claim. docs/adr/0002: a Super Admin
    /// principal must never link a Google identity. Checked here in addition to
    /// <c>GoogleSignInService.CompleteSignInAsync</c>'s own check, since this method is an
    /// independently reachable API surface.
    /// </summary>
    SuperAdminLinkingNotAllowed,

    /// <summary>The current account password was missing or did not verify.</summary>
    InvalidPassword,

    Failed,
}

public sealed record GoogleAccountLinkResult(GoogleAccountLinkOutcome Outcome);
