namespace Davetiye.Application.Modules.Templates.Contracts;

/// <summary>
/// Compiled-code boundary for renderer availability.  A registry entry deliberately identifies a
/// renderer without transporting React, HTML, CSS or JavaScript through the database or API.
/// </summary>
public interface ITemplateRendererRegistry
{
    IReadOnlyCollection<TemplateRendererRegistration> Registrations { get; }

    TemplateRendererRegistration? Resolve(string templateKey, int rendererVersion);
}

public sealed record TemplateRendererRegistration(string TemplateKey, int RendererVersion);
