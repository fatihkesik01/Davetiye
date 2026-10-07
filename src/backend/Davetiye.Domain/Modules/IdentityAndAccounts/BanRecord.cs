namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// Schema for a Super Admin ban action against an Account, per docs/PRODUCT.md §25 ("Ban Sistemi")
/// and docs/PHASE_0_PLAN.md §3 ("Account 1..* BanRecord"). P9-M2 application services and
/// request-time gates enforce login/session invalidation and PII-free public unavailability;
/// this entity stores the durable ban/revocation record.
///
/// <see cref="RevokedAt"/> is nullable so a ban can be lifted while preserving its history.
/// MVP unban authorization is accepted in PD-11 (docs/PHASE_0_PLAN.md §12); application services
/// and request-time gates enforce the accepted access/session behavior.
/// </summary>
public sealed class BanRecord
{
    public const int MaxInternalNoteLength = 2000;

    private BanRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>Optional private operational note; never copy this value to audit records.</summary>
    public string? InternalNote { get; private set; }

    public DateTimeOffset BannedAt { get; private set; }

    /// <summary>
    /// The actor (Super Admin principal) who issued the ban. Deliberately a bare id, not a
    /// navigation: the Super Admin principal/bootstrap model is out of this milestone's scope.
    /// </summary>
    public Guid BannedByActorId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public long Revision { get; private set; }

    public static BanRecord Create(
        Guid id,
        Guid accountId,
        string reason,
        DateTimeOffset bannedAt,
        Guid bannedByActorId,
        string? internalNote = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ban record id must not be empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Ban record must reference an account.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Ban reason is required.", nameof(reason));
        }

        if (bannedByActorId == Guid.Empty)
        {
            throw new ArgumentException("Ban record must reference the actor who issued it.", nameof(bannedByActorId));
        }

        var normalizedNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim();
        if (normalizedNote?.Length > MaxInternalNoteLength)
        {
            throw new ArgumentException(
                $"Internal note cannot exceed {MaxInternalNoteLength} characters.", nameof(internalNote));
        }

        return new BanRecord
        {
            Id = id,
            AccountId = accountId,
            Reason = reason.Trim(),
            InternalNote = normalizedNote,
            BannedAt = bannedAt,
            BannedByActorId = bannedByActorId
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("Ban record is already revoked.");
        }

        RevokedAt = revokedAt;
    }

    /// <summary>Removes free-text PII while preserving the immutable ban/audit event.</summary>
    public void RedactForAccountDeletion()
    {
        const string redactedReason = "Account deleted";
        if (Reason == redactedReason && InternalNote is null) return;
        Reason = redactedReason;
        InternalNote = null;
        Revision++;
    }
}
