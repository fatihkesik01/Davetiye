using Davetiye.Application.Modules.Templates.Contracts;

namespace Davetiye.Infrastructure.Modules.Templates;

/// <summary>
/// Every renderer version that the deployed application can still resolve. This list is deliberately
/// independent from the catalog's current pin: when a template moves to v2, its v1 registration stays
/// here for already-published snapshots while the catalog moves only the current selection to v2.
/// A cross-stack contract test compares this list with the React renderer manifest.
/// </summary>
public static class CodeOwnedTemplateRendererVersions
{
    public static IReadOnlyCollection<TemplateRendererRegistration> Registrations { get; } =
    [
        new("zamansiz-dugun", 1),
        new("romantik-nisan", 1),
        new("gece-kina", 1),
        new("neseli-sunnet", 1),
        new("renkli-dogum-gunu", 1),
        new("pastel-baby-shower", 1),
        new("modern-mezuniyet", 1),
        new("minimal-acilis", 1),
        new("zamansiz-dugun", 2),
        new("romantik-nisan", 2),
        new("gece-kina", 2),
        new("neseli-sunnet", 2),
        new("renkli-dogum-gunu", 2),
        new("pastel-baby-shower", 2),
        new("modern-mezuniyet", 2),
        new("minimal-acilis", 2)
    ];
}
