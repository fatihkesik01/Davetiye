using Davetiye.Application.Modules.Templates.Contracts;

namespace Davetiye.Infrastructure.Modules.Templates;

public sealed class TemplateRendererRegistry : ITemplateRendererRegistry
{
    private readonly IReadOnlyDictionary<(string Key, int Version), TemplateRendererRegistration> registrations =
        CodeOwnedTemplateRendererVersions.Registrations.ToDictionary(
            registration => (registration.TemplateKey, registration.RendererVersion),
            registration => registration);

    public IReadOnlyCollection<TemplateRendererRegistration> Registrations =>
        CodeOwnedTemplateRendererVersions.Registrations;

    public TemplateRendererRegistration? Resolve(string templateKey, int rendererVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        return registrations.GetValueOrDefault((templateKey.Trim(), rendererVersion));
    }
}
