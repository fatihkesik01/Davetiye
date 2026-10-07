using System.Text.Json.Serialization;

namespace Davetiye.Application.Modules.IdentityAndAccounts.Contracts;

public sealed record AdminBannedAccountListItem(
    Guid AccountId,
    string DisplayName,
    AdminBannedAccountType AccountType,
    string? Email,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset BannedAtUtc,
    string Reason);

[JsonConverter(typeof(JsonStringEnumConverter<AdminBannedAccountType>))]
public enum AdminBannedAccountType
{
    [JsonStringEnumMemberName("individual")]
    Individual,
    [JsonStringEnumMemberName("organization")]
    Organization,
}

public sealed record AdminBannedAccountPage(
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<AdminBannedAccountListItem> Items);

public sealed record AdminBannedAccountSearchRequest
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? EmailPrefix { get; init; }
}

public interface IAdminBannedAccountListReader
{
    Task<AdminBannedAccountPage> GetPageAsync(
        int page,
        int pageSize,
        string? emailPrefix,
        CancellationToken cancellationToken);
}
