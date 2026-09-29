namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>
/// The Creator/Davetiye Sahibi account per docs/PRODUCT.md §2-3. Exactly one Account may exist
/// per authenticating Identity user (docs/PHASE_0_BASELINE.md §3: "IdentityUser 0..1 Account").
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
}
