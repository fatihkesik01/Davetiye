namespace Davetiye.Infrastructure.Security;

/// <summary>
/// Configures the lifespan of ASP.NET Core Identity's default <c>DataProtectorTokenProvider</c>
/// tokens (email confirmation and password reset both use it). ASP.NET Core's own default is a
/// 1-day lifespan; docs/THREAT_MODEL.md §9 requires these tokens be short-lived, so this milestone
/// deliberately picks a much shorter default and a hard ceiling well below the framework default,
/// rather than silently keeping it.
/// </summary>
public sealed class EmailTokenOptions
{
    public const string SectionName = "EmailTokens";

    public int TokenLifetimeMinutes { get; init; } = 60;
}
