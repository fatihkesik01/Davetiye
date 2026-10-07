using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class FreeGrantReservationStore(
    DavetiyeDbContext dbContext,
    IAccountQuotaTransactionRunner transactionRunner) : IFreeGrantReservationStore
{
    public Task<FreeGrantReservationResult> TryReserveAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken) =>
        transactionRunner.ExecuteAsync(
            accountId,
            token => ReserveCoreAsync(accountId, invitationId, reservedAt, consume: false, token),
            cancellationToken);

    public Task<FreeGrantReservationResult> TryConsumeAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken) =>
        transactionRunner.ExecuteAsync(
            accountId,
            token => ReserveCoreAsync(accountId, invitationId, consumedAt, consume: true, token),
            cancellationToken);

    public Task<FreeGrantReservationResult> TryConsumeExistingReservationAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken) =>
        transactionRunner.ExecuteAsync(accountId, async token =>
        {
            // A reservation accepted while the plan was active remains consumable at its start;
            // later catalog deactivation is not a retroactive revoke (P3-M1 PD-03).
            var freePlanId = await FindFreePlanIdForExistingReservationAsync(token);
            if (freePlanId is null)
            {
                return new FreeGrantReservationResult(
                    FreeGrantReservationOutcome.FreePlanUnavailable,
                    null);
            }

            var grant = await dbContext.AccountPlanGrants.SingleOrDefaultAsync(
                candidate => candidate.AccountId == accountId && candidate.Source == GrantSource.Free,
                token);
            if (grant is null || grant.AssignedInvitationId is null || grant.ReservedAt is null)
            {
                return new FreeGrantReservationResult(
                    FreeGrantReservationOutcome.ReservationNotFound,
                    grant?.Id);
            }

            if (grant.PlanId != freePlanId.Value)
            {
                return new FreeGrantReservationResult(
                    FreeGrantReservationOutcome.InvalidConfiguration,
                    grant.Id);
            }

            if (grant.RevokedAt is not null)
            {
                return new FreeGrantReservationResult(FreeGrantReservationOutcome.Revoked, grant.Id);
            }

            if (grant.ConsumedAt is not null)
            {
                return new FreeGrantReservationResult(
                    grant.AssignedInvitationId == invitationId
                        ? FreeGrantReservationOutcome.Reserved
                        : FreeGrantReservationOutcome.AlreadyConsumed,
                    grant.Id);
            }

            if (grant.AssignedInvitationId != invitationId)
            {
                return new FreeGrantReservationResult(
                    FreeGrantReservationOutcome.ReservedForAnotherInvitation,
                    grant.Id);
            }

            grant.ConsumeForInvitation(invitationId, consumedAt);
            await dbContext.SaveChangesAsync(token);
            return new FreeGrantReservationResult(FreeGrantReservationOutcome.Reserved, grant.Id);
        }, cancellationToken);

    private async Task<FreeGrantReservationResult> ReserveCoreAsync(
        Guid accountId,
        Guid invitationId,
        DateTimeOffset instant,
        bool consume,
        CancellationToken cancellationToken)
    {
        var freePlan = await dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.Key == "free")
            .Select(plan => new
            {
                plan.Id,
                plan.IsActive,
                plan.BillingKind
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (freePlan is null)
        {
            return new FreeGrantReservationResult(
                FreeGrantReservationOutcome.FreePlanUnavailable,
                null);
        }

        // Source-to-plan mapping is an admission invariant, not merely a resolver concern. Check
        // it before availability so a malformed active or inactive `free` row fails closed as an
        // invalid configuration and can never create/consume a lifetime grant.
        if (freePlan.BillingKind != PlanBillingKind.Free)
        {
            return new FreeGrantReservationResult(
                FreeGrantReservationOutcome.InvalidConfiguration,
                null);
        }

        if (!freePlan.IsActive)
        {
            return new FreeGrantReservationResult(
                FreeGrantReservationOutcome.FreePlanUnavailable,
                null);
        }

        var freePlanId = freePlan.Id;
        var grant = await dbContext.AccountPlanGrants.SingleOrDefaultAsync(
            candidate => candidate.AccountId == accountId && candidate.Source == GrantSource.Free,
            cancellationToken);
        if (grant is null)
        {
            grant = AccountPlanGrant.Create(
                Guid.NewGuid(), accountId, freePlanId, GrantSource.Free, instant);
            dbContext.AccountPlanGrants.Add(grant);
        }
        else if (grant.PlanId != freePlanId)
        {
            return new FreeGrantReservationResult(
                FreeGrantReservationOutcome.InvalidConfiguration,
                grant.Id);
        }

        if (grant.RevokedAt is not null)
        {
            return new FreeGrantReservationResult(FreeGrantReservationOutcome.Revoked, grant.Id);
        }

        if (grant.ConsumedAt is not null)
        {
            if (consume && grant.AssignedInvitationId == invitationId)
            {
                return new FreeGrantReservationResult(FreeGrantReservationOutcome.Reserved, grant.Id);
            }

            return new FreeGrantReservationResult(FreeGrantReservationOutcome.AlreadyConsumed, grant.Id);
        }

        if (grant.AssignedInvitationId is not null && grant.AssignedInvitationId != invitationId)
        {
            return new FreeGrantReservationResult(
                FreeGrantReservationOutcome.ReservedForAnotherInvitation,
                grant.Id);
        }

        if (consume)
        {
            grant.ConsumeForInvitation(invitationId, instant);
        }
        else if (grant.AssignedInvitationId is null)
        {
            grant.ReserveForInvitation(invitationId, instant);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new FreeGrantReservationResult(FreeGrantReservationOutcome.Reserved, grant.Id);
    }

    private Task<Guid?> FindFreePlanIdForExistingReservationAsync(
        CancellationToken cancellationToken) =>
        dbContext.Plans
            .AsNoTracking()
            // Scheduled admission already validated the plan shape. Later catalog changes are not
            // retroactive revocation, so start-time consumption intentionally resolves by the
            // accepted plan key/id without reapplying active/billing admission checks (PD-03).
            .Where(plan => plan.Key == "free")
            .Select(plan => (Guid?)plan.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
