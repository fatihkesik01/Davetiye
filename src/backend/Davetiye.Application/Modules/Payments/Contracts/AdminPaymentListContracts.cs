namespace Davetiye.Application.Modules.Payments.Contracts;

public sealed record AdminPaymentListItem(
    Guid Id,
    string Reference,
    string Status,
    string PlanKey,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ReversalKind,
    DateTimeOffset? ReversedAtUtc);

/// <summary>
/// A live, offset-paginated operational feed. Each response has a deterministic order, but rows may
/// move between page requests when their payment status and UpdatedAt timestamp change.
/// </summary>
public sealed record AdminPaymentPage(
    int Page,
    int PageSize,
    long TotalCount,
    IReadOnlyList<AdminPaymentListItem> Items);

public interface IAdminPaymentListReader
{
    Task<AdminPaymentPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
