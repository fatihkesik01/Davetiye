namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// Schema for a Super Admin ban action against an Account, per docs/PRODUCT.md §25 ("Ban Sistemi")
/// and docs/PHASE_0_BASELINE.md §3 ("Account 1..* BanRecord"). This is schema only: session
/// invalidation, login blocking and the public-gate PII-free-unavailable response are M6's
/// enforcement job, not modeled here.
///
/// <see cref="RevokedAt"/> is nullable so a ban can structurally be lifted. Whether admin unban is
/// an enabled *feature* in the MVP product is PD-11, which is unresolved
/// (docs/PHASE_0_BASELINE.md §12) — this schema does not decide that; it only avoids foreclosing
/// either answer.
/// </summary>
public sealed class BanRecord
{
    private BanRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

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
        Guid bannedByActorId)
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

        return new BanRecord
        {
            Id = id,
            AccountId = accountId,
            Reason = reason.Trim(),
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
}
