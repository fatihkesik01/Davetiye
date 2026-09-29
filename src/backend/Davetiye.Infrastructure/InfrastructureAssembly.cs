using System.Reflection;

namespace Davetiye.Infrastructure;

public static class InfrastructureAssembly
{
    public static Assembly Value { get; } = typeof(InfrastructureAssembly).Assembly;
}
