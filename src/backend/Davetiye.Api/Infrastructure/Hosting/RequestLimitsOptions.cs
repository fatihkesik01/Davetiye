namespace Davetiye.Api.Infrastructure.Hosting;

/// <summary>
/// Configurable request body size limit for this API. Media (photo/video) uploads never flow
/// through this process; they use short-lived direct-to-Cloudflare capabilities per ADR-0005.
/// This limit only needs to comfortably cover JSON/API request payloads.
/// </summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    public const long DefaultMaxRequestBodyBytes = 1_048_576;

    public long MaxRequestBodyBytes { get; init; } = DefaultMaxRequestBodyBytes;
}
