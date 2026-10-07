namespace Davetiye.Application.Modules.Administration.Contracts;

/// <summary>Aggregate-only Super Admin dashboard projection. Contains no resource identifiers or user data.</summary>
public sealed record AdminOverview(
    DateTimeOffset GeneratedAtUtc,
    AdminOverviewAccounts Accounts,
    AdminOverviewInvitations Invitations,
    AdminOverviewPlans Plans,
    AdminOverviewGrants Grants,
    AdminOverviewPayments Payments,
    AdminOverviewStorage Storage,
    AdminOverviewHealth Health);

public sealed record AdminOverviewAccounts(long Total, long Individual, long Organization, long Banned);

public sealed record AdminOverviewInvitations(long Draft, long Scheduled, long Active, long Paused, long Expired, long Deleted);

public sealed record AdminOverviewPlans(long Total, long Active, long Inactive);

public sealed record AdminOverviewGrants(long Total, long Free, long IndividualPurchase, long OrganizationSubscription, long Revoked);

public sealed record AdminOverviewPayments(long Pending, long Unknown, long Succeeded, long Failed, long Canceled, long Reversed);

public sealed record AdminOverviewStorage(
    long Assets,
    long Ready,
    long PendingUpload,
    long Processing,
    long PendingDeletion,
    long Deleted,
    long Rejected,
    long VerifiedBytes);

public sealed record AdminOverviewHealth(string Api, string Database);

public interface IAdminOverviewHealthReader
{
    Task<AdminOverviewHealth> GetAsync(CancellationToken cancellationToken);
}

public interface IAdminOverviewService
{
    Task<AdminOverview> GetAsync(CancellationToken cancellationToken);
}
