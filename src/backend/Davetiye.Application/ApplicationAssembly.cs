using System.Reflection;

namespace Davetiye.Application;

public static class ApplicationAssembly
{
    public static Assembly Value { get; } = typeof(ApplicationAssembly).Assembly;
}
