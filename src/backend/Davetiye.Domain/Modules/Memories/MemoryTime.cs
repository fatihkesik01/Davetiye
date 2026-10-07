namespace Davetiye.Domain.Modules.Memories;

internal static class MemoryTime
{
    public static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Memory timestamps must be UTC.", parameterName);
    }
}
