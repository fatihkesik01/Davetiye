using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class AdminPaymentListReader(DavetiyeDbContext db) : IAdminPaymentListReader
{
    public async Task<AdminPaymentPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = GetOffset(page, pageSize);
        var query = db.PaymentAttempts.AsNoTracking();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(attempt => attempt.UpdatedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(attempt => new AdminPaymentListItem(
                attempt.Id,
                attempt.Reference,
                attempt.Status,
                attempt.PlanKey,
                attempt.Amount,
                attempt.Currency,
                attempt.CreatedAt,
                attempt.UpdatedAt,
                attempt.ReversalKind,
                attempt.ReversedAtUtc))
            .ToListAsync(cancellationToken);

        return new AdminPaymentPage(page, pageSize, totalCount, items);
    }

    private static int GetOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page is outside the supported range.");

        return (page - 1) * pageSize;
    }
}
