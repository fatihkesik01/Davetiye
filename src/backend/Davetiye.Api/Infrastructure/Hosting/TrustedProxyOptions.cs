namespace Davetiye.Api.Infrastructure.Hosting;

/// <summary>
/// Configures which upstream reverse proxies (Nginx per docs/DEPLOYMENT.md) are trusted to set
/// X-Forwarded-For/X-Forwarded-Proto. Deliberately empty by default: no proxy is trusted, and
/// therefore no forwarded header is honored, until an operator explicitly configures one. This
/// is a fail-closed default, not a hardcoded proxy address.
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxies";

    /// <summary>CIDR networks, e.g. "127.0.0.1/32" or "::1/128".</summary>
    public string[] Networks { get; init; } = [];

    public int ForwardLimit { get; init; } = 1;
}
