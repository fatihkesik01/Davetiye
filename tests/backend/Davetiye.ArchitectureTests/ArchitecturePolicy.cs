namespace Davetiye.ArchitectureTests;

internal static class ArchitecturePolicy
{
    private static readonly string[] ForbiddenContractSegments =
    [
        "Internal",
        "Implementation",
        "Implementations",
        "Handler",
        "Handlers"
    ];

    private static readonly string[] ProviderNames =
    [
        "Cloudflare",
        "Iyzico",
        "Resend"
    ];

    public static bool IsProviderNeutral(string value) =>
        !ProviderNames.Any(provider =>
            value.Contains(provider, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidModuleNamespace(string value, string layerNamespace)
    {
        var prefix = $"{layerNamespace}.Modules.";

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = value[prefix.Length..];
        var moduleName = remainder.Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        return moduleName.Length > 0 && char.IsUpper(moduleName[0]);
    }

    public static bool IsApplicationContractNamespace(string value)
    {
        const string prefix = "Davetiye.Application.Modules.";

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = value[prefix.Length..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2 &&
            segments[1] == "Contracts" &&
            !segments.Skip(2).Any(IsForbiddenContractSegment);
    }

    public static bool IsAllowedSharedKernelTypeName(string value) =>
        value.EndsWith("Id", StringComparison.Ordinal) ||
        value is "IClock" or "SystemClock" or "Money" or "Result" ||
        value.StartsWith("Result`", StringComparison.Ordinal);

    public static bool IsAllowedContractTypeName(string value) =>
        !ForbiddenContractSegments.Any(segment =>
            value.Contains(segment, StringComparison.OrdinalIgnoreCase));

    public static string? ValidateModuleSource(
        string layerName,
        string relativePath,
        string sourceText)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length < 3 || pathSegments[0] != "Modules")
        {
            return $"Module source must use Modules/<ModuleName>/...: {relativePath}";
        }

        var moduleName = pathSegments[1];
        if (moduleName.Length == 0 || !char.IsUpper(moduleName[0]))
        {
            return $"Module folder must start with an uppercase letter: {relativePath}";
        }

        var namespaceDeclaration = ReadNamespace(sourceText);
        if (namespaceDeclaration is null)
        {
            return $"Module source has no namespace: {relativePath}";
        }

        var namespaceSegments = pathSegments[..^1];
        var expectedNamespace = $"Davetiye.{layerName}." + string.Join('.', namespaceSegments);

        if (namespaceDeclaration != expectedNamespace &&
            !namespaceDeclaration.StartsWith($"{expectedNamespace}.", StringComparison.Ordinal))
        {
            return $"Expected namespace {expectedNamespace}, found {namespaceDeclaration}: {relativePath}";
        }

        return null;
    }

    private static bool IsForbiddenContractSegment(string value) =>
        ForbiddenContractSegments.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static string? ReadNamespace(string sourceText)
    {
        const string namespaceKeyword = "namespace ";
        var declaration = sourceText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(namespaceKeyword, StringComparison.Ordinal));

        return declaration?[namespaceKeyword.Length..]
            .Trim()
            .TrimEnd(';', '{')
            .Trim();
    }
}
