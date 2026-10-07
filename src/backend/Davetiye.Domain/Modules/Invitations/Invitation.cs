namespace Davetiye.Domain.Modules.Invitations;

/// <summary>
/// Creator-owned invitation aggregate root. Account and public identifiers are immutable. The
/// public code is a locator only; management authorization always uses <see cref="AccountId"/>.
/// </summary>
public sealed class Invitation
{
    private Invitation()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Immutable, globally unique public locator generated from a CSPRNG.</summary>
    public string PublicCode { get; private set; } = string.Empty;

    public InvitationStoredState State { get; private set; }

    /// <summary>Current working template pin. The published snapshot owns an independent copy.</summary>
    public string? TemplateKey { get; private set; }

    public int? RendererVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Soft-deletion overlay. Lifecycle delete/restore commands are implemented in M6.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Inert retention companion required by the shared soft-delete schema convention.</summary>
    public DateTimeOffset? PurgeAfter { get; private set; }

    public long Revision { get; private set; }

    public static Invitation Create(
        Guid id,
        Guid accountId,
        string publicCode,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must not be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Invitation must reference an owning account.", nameof(accountId));
        }

        PublicInvitationCode.EnsureValid(publicCode, nameof(publicCode));

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Invitation creation time must be expressed in UTC.", nameof(createdAt));
        }

        return new Invitation
        {
            Id = id,
            AccountId = accountId,
            PublicCode = publicCode,
            State = InvitationStoredState.Draft,
            CreatedAt = createdAt
        };
    }

    public void PinTemplate(string templateKey, int rendererVersion)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            throw new ArgumentException("Template key is required to pin a template.", nameof(templateKey));
        }

        var normalizedTemplateKey = templateKey.Trim();
        if (normalizedTemplateKey.Length > 100)
        {
            throw new ArgumentException("Template key must not exceed 100 characters.", nameof(templateKey));
        }

        if (rendererVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rendererVersion), rendererVersion, "Renderer version must be a positive number.");
        }

        // M4's owner-scoped command evaluates stored state + current window + UTC clock before
        // calling this method. Stored Scheduled alone cannot enforce the template lock because it
        // may be either pre-start (editable Working pin) or effective Active (locked).
        TemplateKey = normalizedTemplateKey;
        RendererVersion = rendererVersion;
        Revision++;
    }

    /// <summary>Initial publication transition. Later lifecycle transitions belong to P3-M4.</summary>
    public void BeginInitialPublication(bool scheduled)
    {
        if (State != InvitationStoredState.Draft)
        {
            throw new InvalidOperationException("Only a Draft invitation can begin initial publication.");
        }

        if (TemplateKey is null || RendererVersion is null)
        {
            throw new InvalidOperationException("A template must be pinned before publication.");
        }

        State = scheduled ? InvitationStoredState.Scheduled : InvitationStoredState.Active;
        Revision++;
    }

    public void ChangePublicationState(InvitationStoredState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        State = state;
        Revision++;
    }

    public void MoveToTrash(DateTimeOffset deletedAt, DateTimeOffset purgeAfter)
    {
        if (deletedAt.Offset != TimeSpan.Zero || purgeAfter.Offset != TimeSpan.Zero || purgeAfter < deletedAt)
        {
            throw new ArgumentException("Trash timestamps must be UTC with purge at or after deletion.");
        }

        if (DeletedAt is not null) return;
        DeletedAt = deletedAt;
        PurgeAfter = purgeAfter;
        Revision++;
    }

    /// <summary>Moves an invitation into the accepted M2 permanent-purge path immediately.</summary>
    public void SchedulePermanentPurge(DateTimeOffset deletedAtUtc)
    {
        if (deletedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Deletion timestamp must be UTC.", nameof(deletedAtUtc));
        if (DeletedAt is null)
        {
            DeletedAt = deletedAtUtc;
            PurgeAfter = deletedAtUtc;
            Revision++;
            return;
        }
        if (PurgeAfter is null || PurgeAfter > deletedAtUtc)
        {
            PurgeAfter = deletedAtUtc;
            Revision++;
        }
    }

    public void RestoreFromTrash()
    {
        if (DeletedAt is null || PurgeAfter is null)
            throw new InvalidOperationException("Only a retained Trash invitation can be restored.");
        DeletedAt = null;
        PurgeAfter = null;
        State = InvitationStoredState.Draft;
        Revision++;
    }
}
