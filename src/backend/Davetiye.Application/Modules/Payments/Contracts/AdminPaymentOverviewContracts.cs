namespace Davetiye.Application.Modules.Payments.Contracts;

public sealed record AdminPaymentOverview(long Pending, long Unknown, long Succeeded, long Failed, long Canceled, long Reversed);

public interface IAdminPaymentOverviewReader
{
    Task<AdminPaymentOverview> GetAsync(CancellationToken cancellationToken);
}
