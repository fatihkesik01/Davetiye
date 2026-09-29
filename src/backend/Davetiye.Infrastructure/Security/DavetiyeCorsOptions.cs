namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Exact-origin CORS allowlist read from configuration (docs/THREAT_MODEL.md §5: "CORS yalnız
/// runtime-config exact origin listesiyle ... çalışır; wildcard kullanılmaz"). Named
/// <c>DavetiyeCorsOptions</c> rather than <c>CorsOptions</c> to avoid colliding with
/// <see cref="Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions"/> in the same DI setup.
/// </summary>
public sealed class DavetiyeCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];
}
