namespace Davetiye.Infrastructure.Security;

/// <summary>
/// The trusted, operator-configured base URL used to build email links (email confirmation,
/// password reset). docs/THREAT_MODEL.md §9: "Email link base URL'si Host header'dan değil trusted
/// configuration'dan üretilir" — this is that trusted configuration; no request's <c>Host</c>
/// header is ever used to build a link.
/// </summary>
public sealed class PublicWebOptions
{
    public const string SectionName = "PublicWeb";

    public string BaseUrl { get; init; } = "http://localhost:5173";
}
