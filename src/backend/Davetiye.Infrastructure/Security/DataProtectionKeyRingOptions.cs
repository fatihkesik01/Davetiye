namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Where the ASP.NET Core Data Protection key ring is persisted (docs/THREAT_MODEL.md §5: "ASP.NET
/// Data Protection keys kalıcı, repo dışında ve sınırlı izinlidir"). The default is a path relative
/// to the host's content root rather than a hardcoded OS-specific absolute path, so the same
/// default works on both Windows local dev and Linux deployment; the directory this resolves to
/// (".dataprotection-keys" by default) is listed in .gitignore so key material is never committed,
/// while a real deployment overrides this to a persistent volume path outside the git working tree
/// entirely (docs/DEPLOYMENT.md).
/// </summary>
public sealed class DataProtectionKeyRingOptions
{
    public const string SectionName = "DataProtection";

    public string KeyRingDirectory { get; init; } = ".dataprotection-keys";
}
