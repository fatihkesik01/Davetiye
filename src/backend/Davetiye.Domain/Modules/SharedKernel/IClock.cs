namespace Davetiye.Domain.Modules.SharedKernel;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
