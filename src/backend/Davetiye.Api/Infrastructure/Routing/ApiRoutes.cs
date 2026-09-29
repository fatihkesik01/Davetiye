namespace Davetiye.Api.Infrastructure.Routing;

/// <summary>
/// The versioned API route convention that all future business endpoints follow. Phase 1 adds no
/// business endpoints; <c>Program.cs</c> maps a single non-business diagnostic route under this
/// prefix so the convention itself is real and covered by an integration test.
/// </summary>
public static class ApiRoutes
{
    public const string V1Prefix = "/api/v1";
}
