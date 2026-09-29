using System.Reflection;

namespace Davetiye.Domain;

public static class DomainAssembly
{
    public static Assembly Value { get; } = typeof(DomainAssembly).Assembly;
}
