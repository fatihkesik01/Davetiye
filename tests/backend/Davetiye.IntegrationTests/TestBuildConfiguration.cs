namespace Davetiye.IntegrationTests;

internal static class TestBuildConfiguration
{
#if DEBUG
    public const string Name = "Debug";
#else
    public const string Name = "Release";
#endif
}
