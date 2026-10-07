namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public static class AccountConsentVersions
{
    public const string ServiceNotice = "service-notice-draft-v1";
    public const string MarketingPreference = "marketing-preference-v1";
}

public sealed record MarketingPreferenceUpdateRequest(bool OptedIn);

public sealed record ConsentHistoryItem(string Kind, bool Granted, string Version, string Source, DateTimeOffset RecordedAt);

public sealed record ServiceNoticeConsentSnapshot(
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt,
    string NoticeVersion,
    string TextStatus);

public sealed record MarketingConsentSnapshot(bool OptedIn, DateTimeOffset? UpdatedAt, string Version);

public sealed record AccountConsentSnapshot(
    ServiceNoticeConsentSnapshot ServiceNotice,
    MarketingConsentSnapshot Marketing,
    IReadOnlyCollection<ConsentHistoryItem> History);

public interface IAccountConsentService
{
    Task<AccountConsentSnapshot?> GetAsync(Guid identityUserId, CancellationToken cancellationToken);
    Task<bool> IsServiceNoticeAcknowledgedAsync(Guid identityUserId, CancellationToken cancellationToken);
    Task<AccountConsentSnapshot?> AcknowledgeServiceNoticeAsync(Guid identityUserId, bool acknowledged, CancellationToken cancellationToken);
    Task<AccountConsentSnapshot?> UpdateMarketingPreferenceAsync(
        Guid identityUserId,
        bool optedIn,
        CancellationToken cancellationToken);
}
