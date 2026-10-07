using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.SharedKernel;

namespace Davetiye.Application.Modules.Administration;

/// <summary>Composes owner-approved, count-only projections into the Super Admin overview.</summary>
public sealed class AdminOverviewService(
    IAdminAccountOverviewReader accounts,
    IAdminInvitationOverviewReader invitations,
    IAdminPlanAndGrantOverviewReader plansAndGrants,
    IAdminPaymentOverviewReader payments,
    IAdminMediaOverviewReader media,
    IAdminOverviewHealthReader health,
    IClock clock) : IAdminOverviewService
{
    public async Task<AdminOverview> GetAsync(CancellationToken cancellationToken)
    {
        // All readers share the request-scoped DbContext. Sequential calls avoid concurrent EF
        // operations on that context while preserving clear module ownership.
        var accountCounts = await accounts.GetAsync(cancellationToken);
        var invitationCounts = await invitations.GetAsync(cancellationToken);
        var planCounts = await plansAndGrants.GetAsync(cancellationToken);
        var paymentCounts = await payments.GetAsync(cancellationToken);
        var mediaCounts = await media.GetAsync(cancellationToken);
        var healthCounts = await health.GetAsync(cancellationToken);

        return new AdminOverview(
            clock.UtcNow.ToUniversalTime(),
            new AdminOverviewAccounts(accountCounts.Total, accountCounts.Individual, accountCounts.Organization, accountCounts.Banned),
            new AdminOverviewInvitations(invitationCounts.Draft, invitationCounts.Scheduled, invitationCounts.Active,
                invitationCounts.Paused, invitationCounts.Expired, invitationCounts.Deleted),
            new AdminOverviewPlans(planCounts.PlanTotal, planCounts.ActivePlans, planCounts.InactivePlans),
            new AdminOverviewGrants(planCounts.GrantTotal, planCounts.FreeGrants, planCounts.IndividualPurchaseGrants,
                planCounts.OrganizationSubscriptionGrants, planCounts.RevokedGrants),
            new AdminOverviewPayments(paymentCounts.Pending, paymentCounts.Unknown, paymentCounts.Succeeded,
                paymentCounts.Failed, paymentCounts.Canceled, paymentCounts.Reversed),
            new AdminOverviewStorage(mediaCounts.Assets, mediaCounts.Ready, mediaCounts.PendingUpload,
                mediaCounts.Processing, mediaCounts.PendingDeletion, mediaCounts.Deleted, mediaCounts.Rejected,
                mediaCounts.VerifiedBytes),
            healthCounts);
    }
}
