namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Google OAuth sign-in configuration (docs/PRODUCT.md §3, docs/THREAT_MODEL.md §6). Defaults to
/// disabled: a raw non-localhost IP deployment cannot register a Google web OAuth redirect host
/// (docs/ARCHITECTURE.md §2), so Google sign-in must stay off until an operator explicitly enables it
/// with a real client id/secret and a verified callback base URL. Enabling it in Production without a
/// valid HTTPS, non-IP, non-localhost <see cref="CallbackBaseUrl"/> is a fail-closed startup
/// validation failure (see <see cref="GoogleAuthOptionsValidator"/>), never a silent downgrade to
/// "disabled" — an operator who flips this on in Production is asserting the domain+HTTPS
/// prerequisite already holds, and a startup crash surfaces a genuine misconfiguration immediately
/// instead of quietly serving without Google sign-in.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public bool Enabled { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// The trusted base URL Google's own OAuth handler intercepts directly as part of the
    /// authentication middleware pipeline (its path becomes <c>GoogleOptions.CallbackPath</c>) —
    /// distinct from this API's own <c>/api/v1/auth/google/complete</c> endpoint, which is where the
    /// handler redirects the browser afterwards once the external-login cookie has been established.
    /// Never derived from an inbound request's Host header.
    /// </summary>
    public string CallbackBaseUrl { get; init; } = "http://localhost:5000/api/v1/auth/google/oauth-callback";
}
