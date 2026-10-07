namespace Davetiye.Application.Modules.Templates.Contracts;

/// <summary>
/// Narrow Templates-module read boundary used when an Invitation pins its current working
/// template. M3 deliberately knows no catalog rows and grants no entitlement behavior.
/// </summary>
public interface ITemplateSelectionResolver
{
    Task<TemplateSelection?> ResolveActiveAsync(string templateKey, CancellationToken cancellationToken);
}

public sealed record TemplateSelection(
    string TemplateKey,
    int RendererVersion,
    bool IsPremium,
    IReadOnlyList<string> SupportedModules);
