using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Payments;

public sealed class AdminPaymentOverviewReader(DavetiyeDbContext db) : IAdminPaymentOverviewReader
{
    public async Task<AdminPaymentOverview> GetAsync(CancellationToken cancellationToken)
    {
        var groups = await db.PaymentAttempts.AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var counts = groups.ToDictionary(item => item.Status, item => item.Count, StringComparer.Ordinal);

        return new AdminPaymentOverview(
            Count(PaymentAttemptStatus.Pending),
            Count(PaymentAttemptStatus.Unknown),
            Count(PaymentAttemptStatus.Succeeded),
            Count(PaymentAttemptStatus.Failed),
            Count(PaymentAttemptStatus.Canceled),
            Count(PaymentAttemptStatus.Reversed));

        long Count(string status) => counts.GetValueOrDefault(status);
    }
}
