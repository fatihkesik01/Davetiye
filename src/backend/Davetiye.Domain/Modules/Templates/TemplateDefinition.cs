using System.Text.Json;

namespace Davetiye.Domain.Modules.Templates;

/// <summary>
/// DB-managed catalog metadata for one code-owned template, per docs/PRODUCT.md §5 ("DB'de template
/// key, isim, kategori, aktif/pasif, ücretsiz/premium, preview, desteklenen modüller, gerekli ve
/// önerilen alanlar gibi metadata tutulur ve yönetilebilir") and ADR-0007 ("DB TemplateDefinition
/// içinde stable key, metadata, active, free/premium, preview, desteklenen modüller ve
/// required/recommended field metadata tutar").
///
/// This entity is schema/catalog-only: the actual React renderer is code-owned (ADR-0007, "Renderer
/// registry (templateKey, rendererVersion) ile resolve eder... Kullanıcı/Super Admin executable
/// HTML/CSS/JavaScript yazamaz"), and no seed data or renderer-registry wiring is created by this
/// milestone (that is M4's job - see docs/PHASE_2_PLAN.md). <see cref="SupportedModules"/>,
/// <see cref="RequiredFields"/> and <see cref="RecommendedFields"/> are stored as opaque JSON arrays
/// of string keys, matching how this codebase already treats presentation-facing variable content as
/// opaque JSON decoded by a higher layer rather than typed at the Domain layer (see
/// Davetiye.Domain.Modules.IntegrationFoundation.InboxMessage.Payload and
/// Davetiye.Domain.Modules.Invitations.WorkingContent.Content) - the concrete set of module/field
/// keys is itself a code-owned catalog (ADR-0007), not a DB-enforced schema.
///
/// <see cref="Category"/> is a free-form display label rather than a fixed enum. The accepted Phase
/// 2 starter taxonomy is supplied by the code-owned catalog seed, while the schema remains extensible.
/// <see cref="CurrentRendererVersion"/> bridges DB catalog selection to the compiled registry. Only
/// the code-owned catalog initializer may change it; ordinary metadata management deliberately cannot.
/// </summary>
public sealed class TemplateDefinition
{
    public const int DescriptionMaxLength = 2000;
    public const int NameMaxLength = 200;

    // Parameterless constructor is for EF Core materialization only; application code must go
    // through Create to keep the entity's invariants enforced.
    private TemplateDefinition()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Stable machine key referenced by Invitation.TemplateKey and the renderer registry.
    /// Unique.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public bool IsPremium { get; private set; }

    /// <summary>The renderer version newly selecting Creators are pinned to for this key today. See
    /// the judgment-call note on this type for why this column exists.</summary>
    public int CurrentRendererVersion { get; private set; }

    /// <summary>Catalog/demo preview image location. Not a Media-module asset (provider media upload
    /// is out of Phase 2 scope) - templates are code-owned, so this is a static asset reference.</summary>
    public string? PreviewImageUrl { get; private set; }

    /// <summary>Opaque JSON array of supported feature-module keys (e.g. RSVP, Memories, Gift).</summary>
    public string SupportedModules { get; private set; } = "[]";

    /// <summary>Opaque JSON array of content field keys a complete invitation needs for this
    /// template. Reporting-only in Phase 2 (M7); never a save-time constraint.</summary>
    public string RequiredFields { get; private set; } = "[]";

    /// <summary>Opaque JSON array of content field keys recommended but not required.</summary>
    public string RecommendedFields { get; private set; } = "[]";

    public long Revision { get; private set; }

    public static TemplateDefinition Create(
        Guid id,
        string key,
        string name,
        string category,
        bool isActive,
        bool isPremium,
        int currentRendererVersion,
        string? previewImageUrl,
        string supportedModules,
        string requiredFields,
        string recommendedFields,
        string? description = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Template definition id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Template key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Template name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Template category is required.", nameof(category));
        }

        if (currentRendererVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRendererVersion), currentRendererVersion, "Renderer version must be positive.");
        }

        EnsureValidJsonArray(supportedModules, nameof(supportedModules));
        EnsureValidJsonArray(requiredFields, nameof(requiredFields));
        EnsureValidJsonArray(recommendedFields, nameof(recommendedFields));
        var normalizedDescription = NormalizeDescription(description);

        return new TemplateDefinition
        {
            Id = id,
            Key = key.Trim(),
            Name = name.Trim(),
            Category = category.Trim(),
            IsActive = isActive,
            IsPremium = isPremium,
            CurrentRendererVersion = currentRendererVersion,
            PreviewImageUrl = NormalizePreviewImageUrl(previewImageUrl, nameof(previewImageUrl)),
            SupportedModules = supportedModules,
            RequiredFields = requiredFields,
            RecommendedFields = recommendedFields,
            Description = normalizedDescription
        };
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Revision++;
    }

    /// <summary>
    /// Updates the bounded presentation fields exposed to Super Admin. Stable identity, renderer,
    /// category, premium status, preview, module support and content-field metadata remain code or
    /// catalog owned and cannot be changed through this operation.
    /// </summary>
    public void UpdateAdminMetadata(string name, string? description, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Template name is required.", nameof(name));
        }

        var normalizedName = name.Trim();
        if (normalizedName.Length > NameMaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name), name, $"Template name must be at most {NameMaxLength} characters.");
        }

        var normalizedDescription = NormalizeDescription(description);
        if (Name == normalizedName && Description == normalizedDescription && IsActive == isActive)
        {
            return;
        }

        Name = normalizedName;
        Description = normalizedDescription;
        IsActive = isActive;
        Revision++;
    }

    /// <summary>
    /// Moves the catalog row to the version currently supplied by compiled application code. This
    /// is deliberately separate from DB-managed presentation metadata: renderer availability is a
    /// deploy concern, not an Admin-editable template setting (ADR-0007).
    /// </summary>
    public void SetCurrentRendererVersion(int currentRendererVersion)
    {
        if (currentRendererVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRendererVersion), currentRendererVersion, "Renderer version must be positive.");
        }

        if (CurrentRendererVersion == currentRendererVersion)
        {
            return;
        }

        CurrentRendererVersion = currentRendererVersion;
        Revision++;
    }

    /// <summary>
    /// Replaces DB-managed presentation metadata in one call. Renderer version is intentionally
    /// excluded: only the code-owned initializer may move that pin through
    /// <see cref="SetCurrentRendererVersion"/>.
    /// </summary>
    public void UpdateMetadata(
        string name,
        string category,
        bool isPremium,
        string? previewImageUrl,
        string supportedModules,
        string requiredFields,
        string recommendedFields,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Template name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Template category is required.", nameof(category));
        }

        EnsureValidJsonArray(supportedModules, nameof(supportedModules));
        EnsureValidJsonArray(requiredFields, nameof(requiredFields));
        EnsureValidJsonArray(recommendedFields, nameof(recommendedFields));
        var normalizedDescription = NormalizeDescription(description);

        Name = name.Trim();
        Category = category.Trim();
        IsPremium = isPremium;
        PreviewImageUrl = NormalizePreviewImageUrl(previewImageUrl, nameof(previewImageUrl));
        SupportedModules = supportedModules;
        RequiredFields = requiredFields;
        RecommendedFields = recommendedFields;
        Description = normalizedDescription;
        Revision++;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalized = description.Trim();
        if (normalized.Length > DescriptionMaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description), description, $"Template description must be at most {DescriptionMaxLength} characters.");
        }

        return normalized;
    }

    private static void EnsureValidJsonArray(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Value must be a syntactically valid JSON array.", paramName, exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Value must be a JSON array.", paramName);
            }
        }
    }

    private static string? NormalizePreviewImageUrl(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.Contains('\\') ||
            normalized.Any(char.IsControl) ||
            !Uri.TryCreate(normalized, UriKind.Relative, out _))
        {
            throw new ArgumentException(
                "Preview image URL must be a safe root-relative application asset path.",
                paramName);
        }

        return normalized;
    }
}
