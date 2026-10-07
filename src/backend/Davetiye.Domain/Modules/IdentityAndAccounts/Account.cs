namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// The Creator/Davetiye Sahibi account per docs/PRODUCT.md §2-3. Exactly one Account may exist
/// per authenticating Identity user (docs/PHASE_0_PLAN.md §3: "IdentityUser 0..1 Account").
/// This entity intentionally holds no reference to the ASP.NET Core Identity user type: Identity
/// is a framework/persistence concern owned by Infrastructure, and Domain stays framework-free.
/// The link is a plain <see cref="IdentityUserId"/> value; the DB-level uniqueness/FK constraint
/// that actually enforces "at most one Account per IdentityUser" is configured in
/// Davetiye.Infrastructure.Modules.IdentityAndAccounts.
/// </summary>
public sealed class Account
{
    // Parameterless constructor is for EF Core materialization only; application code must go
    // through Create to keep the entity's invariants enforced.
    private Account()
    {
    }

    public Guid Id { get; private set; }

    public Guid IdentityUserId { get; private set; }

    public AccountType AccountType { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public long Revision { get; private set; }

    /// <summary>Set when email-verified deletion is confirmed. Any non-null value closes account access.</summary>
    public DateTimeOffset? DeletionStartedAtUtc { get; private set; }

    /// <summary>Set after identity sanitization and durable invitation/cancellation work scheduling.</summary>
    public DateTimeOffset? DeletionCompletedAtUtc { get; private set; }

    public static Account Create(
        Guid id,
        Guid identityUserId,
        AccountType accountType,
        string displayName,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(id));
        }

        if (identityUserId == Guid.Empty)
        {
            throw new ArgumentException("Account must reference an Identity user.", nameof(identityUserId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Account display name is required.", nameof(displayName));
        }

        return new Account
        {
            Id = id,
            IdentityUserId = identityUserId,
            AccountType = accountType,
            DisplayName = displayName.Trim(),
            CreatedAt = createdAt
        };
    }

    public bool BeginDeletion(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (DeletionStartedAtUtc is not null)
        {
            return false;
        }

        DeletionStartedAtUtc = nowUtc;
        Revision++;
        return true;
    }

    public bool AnonymizeForDeletion()
    {
        if (DeletionStartedAtUtc is null)
        {
            throw new InvalidOperationException("Account deletion must be started before profile anonymization.");
        }

        const string anonymizedName = "Deleted account";
        if (DisplayName == anonymizedName)
        {
            return false;
        }

        DisplayName = anonymizedName;
        Revision++;
        return true;
    }

    public bool CompleteDeletion(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (DeletionStartedAtUtc is null)
        {
            throw new InvalidOperationException("Account deletion must be started before it can complete.");
        }

        if (DeletionCompletedAtUtc is not null)
        {
            return false;
        }

        DeletionCompletedAtUtc = nowUtc;
        Revision++;
        return true;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }
    }
}
