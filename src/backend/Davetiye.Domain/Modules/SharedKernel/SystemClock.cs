namespace Davetiye.Domain.Modules.SharedKernel;

/// <summary>
/// The production <see cref="IClock"/> implementation. It is framework-free (plain BCL
/// <see cref="DateTimeOffset"/>), so it can live in Domain/SharedKernel without pulling in any
/// provider or ASP.NET Core dependency; <see cref="ArchitecturePolicy"/>'s SharedKernel allowlist
/// (Davetiye.ArchitectureTests) explicitly permits this type by name.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
