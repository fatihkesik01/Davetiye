using Davetiye.Domain.Modules.PlansAndEntitlements;

namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

public sealed record AdminPlanEntitlementItem(string Key, long? NumericValue, bool? BooleanValue);

public sealed record AdminPlanItem(
    Guid Id,
    string Key,
    string DisplayName,
    string? Description,
    decimal PriceAmount,
    string Currency,
    PlanBillingKind BillingKind,
    IReadOnlyList<AdminPlanEntitlementItem> Entitlements,
    long Revision);

public sealed record AdminPlanUpdateRequest(
    long ExpectedRevision,
    string DisplayName,
    string? Description,
    decimal PriceAmount,
    PlanBillingKind BillingKind,
    IReadOnlyList<AdminPlanEntitlementItem>? Entitlements);

public enum AdminPlanUpdateOutcome { Succeeded, NotFound, Conflict, InvalidRequest }

public sealed record AdminPlanUpdateResult(AdminPlanUpdateOutcome Outcome, AdminPlanItem? Plan = null);

public interface IAdminPlanService
{
    Task<IReadOnlyList<AdminPlanItem>> ListAsync(CancellationToken cancellationToken);
    Task<AdminPlanUpdateResult> UpdateAsync(Guid actorId, Guid planId, AdminPlanUpdateRequest request, CancellationToken cancellationToken);
}
