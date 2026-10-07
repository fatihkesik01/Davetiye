namespace Davetiye.Infrastructure.Modules.Templates;

/// <summary>
/// The compiled catalog mirrors the frontend's eventual code-owned renderer set. It is intentionally
/// limited to renderer identity plus safe catalog metadata: template executable code never enters
/// TemplateDefinition or a database row (ADR-0007).
/// </summary>
public static class CodeOwnedTemplateCatalog
{
    public static IReadOnlyList<CodeOwnedTemplateDefinition> Definitions { get; } =
    [
        new("zamansiz-dugun", "Zamansız Düğün", "Düğün", false, 2, "/template-previews/zamansiz-dugun.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"rsvp\",\"gallery\",\"timeline\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"hostNames\"]"),
        new("romantik-nisan", "Romantik Nişan", "Nişan", true, 2, "/template-previews/romantik-nisan.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"rsvp\",\"giftRegistry\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"hostNames\",\"venue.address\"]"),
        new("gece-kina", "Gece Kınası", "Kına", true, 2, "/template-previews/gece-kina.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"timeline\",\"rsvp\",\"transportation\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"hostNames\",\"programItems\"]"),
        new("neseli-sunnet", "Neşeli Sünnet", "Sünnet", false, 2, "/template-previews/neseli-sunnet.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"rsvp\",\"transportation\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"hostNames\",\"venue.address\"]"),
        new("renkli-dogum-gunu", "Renkli Doğum Günü", "Doğum Günü", false, 2, "/template-previews/renkli-dogum-gunu.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"rsvp\",\"qr\"]",
            "[\"headline\",\"startsAt\"]", "[\"message\",\"venue.name\",\"venue.address\"]"),
        new("pastel-baby-shower", "Pastel Baby Shower", "Baby Shower", true, 2, "/template-previews/pastel-baby-shower.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"rsvp\",\"giftRegistry\",\"qr\"]",
            "[\"headline\",\"startsAt\"]", "[\"message\",\"hostNames\",\"venue.name\"]"),
        new("modern-mezuniyet", "Modern Mezuniyet", "Mezuniyet", false, 2, "/template-previews/modern-mezuniyet.jpg",
            "[\"hero\",\"dateVenue\",\"countdown\",\"calendar\",\"contact\",\"gallery\",\"announcements\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"hostNames\",\"programItems\"]"),
        new("minimal-acilis", "Minimal Açılış", "Açılış/Genel", false, 2, "/template-previews/minimal-acilis.jpg",
            "[\"hero\",\"dateVenue\",\"calendar\",\"contact\",\"announcements\",\"faq\",\"transportation\",\"qr\"]",
            "[\"headline\",\"startsAt\",\"venue.name\"]", "[\"message\",\"venue.address\",\"hostNames\"]")
    ];

}

public sealed record CodeOwnedTemplateDefinition(
    string Key,
    string Name,
    string Category,
    bool IsPremium,
    int RendererVersion,
    string PreviewImageUrl,
    string SupportedModules,
    string RequiredFields,
    string RecommendedFields);
