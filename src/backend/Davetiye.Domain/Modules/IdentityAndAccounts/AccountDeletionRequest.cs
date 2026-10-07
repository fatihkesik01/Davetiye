namespace Davetiye.Domain.Modules.IdentityAndAccounts;

/// <summary>One-time, email-verified account-deletion confirmation. Only a token digest is persisted.</summary>
public sealed class AccountDeletionRequest
{
    private AccountDeletionRequest() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string Purpose { get; private set; } = AccountDeletionRequestPurpose.AccountDeletion;
    public string TokenHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = AccountDeletionRequestStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static AccountDeletionRequest Create(
        Guid id,
        Guid accountId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (id == Guid.Empty || accountId == Guid.Empty)
            throw new ArgumentException("Deletion request and account identifiers must not be empty.");
        if (tokenHash.Length != 64 || !tokenHash.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hexadecimal token digest is required.", nameof(tokenHash));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Deletion confirmation must expire after creation.");

        return new AccountDeletionRequest
        {
            Id = id,
            AccountId = accountId,
            TokenHash = tokenHash.ToLowerInvariant(),
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public bool Consume(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != AccountDeletionRequestStatus.Pending || nowUtc >= ExpiresAtUtc)
            return false;
        Status = AccountDeletionRequestStatus.Consumed;
        ConsumedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public bool Supersede(DateTimeOffset nowUtc) => Close(AccountDeletionRequestStatus.Superseded, nowUtc);

    public bool Expire(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != AccountDeletionRequestStatus.Pending || nowUtc < ExpiresAtUtc)
            return false;
        Status = AccountDeletionRequestStatus.Expired;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    private bool Close(string status, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != AccountDeletionRequestStatus.Pending)
            return false;
        Status = status;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}

public static class AccountDeletionRequestPurpose
{
    public const string AccountDeletion = "AccountDeletion";
}

public static class AccountDeletionRequestStatus
{
    public const string Pending = "Pending";
    public const string Consumed = "Consumed";
    public const string Superseded = "Superseded";
    public const string Expired = "Expired";
}
