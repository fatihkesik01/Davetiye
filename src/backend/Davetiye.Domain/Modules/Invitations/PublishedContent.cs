using System.Text.Json;

namespace Davetiye.Domain.Modules.Invitations;

/// <summary>The one current public snapshot, independent from WorkingContent and its template pin.</summary>
public sealed class PublishedContent
{
    private PublishedContent()
    {
    }

    public Guid Id { get; private set; }

    public Guid InvitationId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public int RendererVersion { get; private set; }

    public int ContentSchemaVersion { get; private set; }

    public string Content { get; private set; } = "{}";

    /// <summary>Immutable Ready media references captured at the explicit publish/update boundary.</summary>
    public string MediaPlacements { get; private set; } = "[]";

    public long SourceWorkingRevision { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Revision { get; private set; }

    public static PublishedContent Create(
        Guid id,
        Guid invitationId,
        string templateKey,
        int rendererVersion,
        int contentSchemaVersion,
        string content,
        long sourceWorkingRevision,
        DateTimeOffset publishedAt,
        string mediaPlacements = "[]")
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Published content id must not be empty.", nameof(id));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Published content must reference an invitation.", nameof(invitationId));
        }

        if (string.IsNullOrWhiteSpace(templateKey))
        {
            throw new ArgumentException("Published content requires a template key.", nameof(templateKey));
        }

        var normalizedTemplateKey = templateKey.Trim();
        if (normalizedTemplateKey.Length > 100)
        {
            throw new ArgumentException("Template key must not exceed 100 characters.", nameof(templateKey));
        }

        if (rendererVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rendererVersion));
        }

        if (contentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentSchemaVersion));
        }

        if (sourceWorkingRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWorkingRevision));
        }

        if (publishedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Published time must be expressed in UTC.", nameof(publishedAt));
        }

        EnsureValidJson(content);
        EnsureValidJson(mediaPlacements);

        return new PublishedContent
        {
            Id = id,
            InvitationId = invitationId,
            TemplateKey = normalizedTemplateKey,
            RendererVersion = rendererVersion,
            ContentSchemaVersion = contentSchemaVersion,
            Content = content,
            MediaPlacements = mediaPlacements,
            SourceWorkingRevision = sourceWorkingRevision,
            PublishedAt = publishedAt,
            UpdatedAt = publishedAt
        };
    }

    public void ReplaceWith(PublishedContent snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.InvitationId != InvitationId || snapshot.PublishedAt < PublishedAt)
        {
            throw new ArgumentException("Replacement must belong to the same invitation and follow its publication time.", nameof(snapshot));
        }

        TemplateKey = snapshot.TemplateKey;
        RendererVersion = snapshot.RendererVersion;
        ContentSchemaVersion = snapshot.ContentSchemaVersion;
        Content = snapshot.Content;
        MediaPlacements = snapshot.MediaPlacements;
        SourceWorkingRevision = snapshot.SourceWorkingRevision;
        UpdatedAt = snapshot.PublishedAt;
        Revision++;
    }

    private static void EnsureValidJson(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Published content must be syntactically valid JSON.", nameof(content), exception);
        }
    }
}
