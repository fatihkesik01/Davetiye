using System.Reflection;

namespace Davetiye.Api;

public static class ApiAssembly
{
    public static Assembly Value { get; } = typeof(ApiAssembly).Assembly;
}
