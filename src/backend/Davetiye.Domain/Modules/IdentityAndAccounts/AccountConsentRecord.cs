namespace Davetiye.Domain.Modules.IdentityAndAccounts;

public enum AccountConsentKind
{
    ServiceNoticeAcknowledgement,
    MarketingPreference,
}

public enum AccountConsentSource
{
    EmailPasswordSignup,
    GoogleSignup,
    AccountSettings,
    ExistingAccountAcknowledgement,
}

/// <summary>
/// Append-only account consent history. Records intentionally contain no consent text or other
/// free-form personal data; legal copy is versioned separately and remains subject to Phase 11 review.
/// </summary>
public sealed class AccountConsentRecord
{
    private AccountConsentRecord() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public AccountConsentKind Kind { get; private set; }
    public bool Granted { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public AccountConsentSource Source { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static AccountConsentRecord Create(
        Guid id,
        Guid accountId,
        AccountConsentKind kind,
        bool granted,
        string version,
        AccountConsentSource source,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || accountId == Guid.Empty)
            throw new ArgumentException("Consent record and account ids must not be empty.");
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(version) || version.Length > 100)
            throw new ArgumentException("Consent version is required and must be at most 100 characters.", nameof(version));
        if (recordedAt == default || recordedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Consent timestamp must be a UTC instant.", nameof(recordedAt));

        return new AccountConsentRecord
        {
            Id = id,
            AccountId = accountId,
            Kind = kind,
            Granted = granted,
            Version = version,
            Source = source,
            RecordedAt = recordedAt,
        };
    }
}
