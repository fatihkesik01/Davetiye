using System.Text.Json;

namespace Davetiye.Domain.Modules.Invitations;

/// <summary>
/// The single current, autosaved draft content snapshot for an Invitation, per
/// docs/PHASE_0_PLAN.md §3 ("Invitation 1──1 WorkingContent... Yalnız bir current WorkingContent ve
/// bir current PublishedContent bulunur; geçmiş revision arşivi oluşturulmaz") and ADR-0003
/// ("Autosave working snapshot'a yazar"). This is the only content snapshot Phase 2 builds -
/// PublishedContent is Phase 3 scope (docs/PHASE_2_PLAN.md's "Kapsam Dışı").
///
/// <see cref="Content"/> is opaque, schema-versioned JSONB (ADR-0001: "Sunuma dönük değişken içerik
/// JSONB olabilir"; the task brief: "Template content is schema-versioned JSONB, validated by backend
/// typed DTOs - not by a database schema per template"). This module only guarantees the stored value
/// is syntactically valid JSON; it never knows or enforces a specific template's field shape, and it
/// never requires the payload to be a complete document - per docs/PHASE_2_PLAN.md an incomplete
/// Draft must be saveable, and required/recommended-field completeness is a reporting concern for a
/// later milestone (M7), not a save-time constraint here. Deep, per-template validation against typed
/// DTOs is the Application layer's job (M3), which is why this stays opaque text at the Domain layer
/// rather than a typed content shape.
///
/// <see cref="Revision"/> is an explicit, application-incremented optimistic-concurrency token (every
/// <see cref="ReplaceContent"/> call bumps it) so that M3's autosave can safely detect two concurrent
/// writers racing on the same Invitation's draft, per docs/PHASE_0_PLAN.md §3's "Invitation ve
/// düzenlenebilir yönetim kayıtlarında explicit monoton revision/concurrency token kullanılır." The DB
/// also marks this column a concurrency token (see
/// Davetiye.Infrastructure.Persistence.ModelBuilderExtensions), so EF Core includes it in every
/// UPDATE's WHERE clause and raises <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// when a save targets a stale revision.
/// </summary>
public sealed class WorkingContent
{
    // Parameterless constructor is for EF Core materialization only; application code must go
    // through Create to keep the entity's invariants enforced.
    private WorkingContent()
    {
    }

    public Guid Id { get; private set; }

    public Guid InvitationId { get; private set; }

    /// <summary>Version of the content JSON shape this payload was written against. Lets the
    /// Application layer pick the right typed DTO/migration when reading an older draft.</summary>
    public int ContentSchemaVersion { get; private set; }

    /// <summary>Raw JSON document. May be sparse/partial - an incomplete Draft is a valid state.</summary>
    public string Content { get; private set; } = "{}";

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Revision { get; private set; }

    public static WorkingContent Create(
        Guid id,
        Guid invitationId,
        int contentSchemaVersion,
        string content,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Working content id must not be empty.", nameof(id));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Working content must reference its owning invitation.", nameof(invitationId));
        }

        if (contentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentSchemaVersion), contentSchemaVersion, "Content schema version must be positive.");
        }

        EnsureValidJson(content, nameof(content));

        return new WorkingContent
        {
            Id = id,
            InvitationId = invitationId,
            ContentSchemaVersion = contentSchemaVersion,
            Content = content,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// Overwrites the draft payload, as autosave does on every accepted write (M3). Bumps
    /// <see cref="Revision"/> so a stale concurrent write is rejected at the DB layer.
    /// </summary>
    public void ReplaceContent(string content, int contentSchemaVersion, DateTimeOffset updatedAt)
    {
        if (contentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentSchemaVersion), contentSchemaVersion, "Content schema version must be positive.");
        }

        EnsureValidJson(content, nameof(content));

        Content = content;
        ContentSchemaVersion = contentSchemaVersion;
        UpdatedAt = updatedAt;
        Revision++;
    }

    private static void EnsureValidJson(string content, string paramName)
    {
        ArgumentNullException.ThrowIfNull(content, paramName);

        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Working content must be syntactically valid JSON.", paramName, exception);
        }
    }
}
