using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.PlansAndEntitlements;

public sealed class PublicationGrantAllocator(
    DavetiyeDbContext dbContext,
    IFreePublicationGrantService freeGrantService,
    IEffectiveEntitlementResolver entitlementResolver) : IPublicationGrantAllocator
{
    public async Task<PublicationGrantAllocationResult> AllocateAsync(
        PublicationGrantAllocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        return request.RequestedGrantId is null
            ? await AllocateFreeAsync(request, cancellationToken)
            : await AllocateRequestedAsync(request, cancellationToken);
    }

    private async Task<PublicationGrantAllocationResult> AllocateFreeAsync(
        PublicationGrantAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var reservation = await freeGrantService.ReserveAsync(
            request.AccountId,
            request.InvitationId,
            request.OccurredAtUtc,
            cancellationToken);
        if (!reservation.IsSuccess || reservation.GrantId is null)
        {
            return PublicationGrantAllocationResult.Denied(Map(reservation.Outcome));
        }

        var resolution = await entitlementResolver.ResolveAsync(
            new EntitlementResolutionContext(
                request.AccountId,
                reservation.GrantId.Value,
                request.InvitationId,
                request.Action),
            cancellationToken);
        if (!resolution.IsGranted || resolution.Entitlements!.GrantSource != GrantSource.Free)
        {
            // A successful reservation is already a mutation. Throwing is deliberate: the outer
            // account transaction rolls it back, whereas returning a denial would commit it.
            throw new InvalidOperationException(
                "The reserved Free grant could not produce a valid entitlement snapshot.");
        }

        var snapshot = resolution.Entitlements;
        if (request.Action == PublicationEntitlementAction.PublishNow)
        {
            var consumption = await freeGrantService.ConsumeImmediatelyAsync(
                request.AccountId,
                request.InvitationId,
                request.OccurredAtUtc,
                cancellationToken);
            if (!consumption.IsSuccess || consumption.GrantId != reservation.GrantId)
            {
                throw new InvalidOperationException(
                    "The reserved Free grant could not be consumed atomically.");
            }

            snapshot = snapshot with
            {
                AssignedInvitationId = request.InvitationId,
                ReservedAt = snapshot.ReservedAt ?? request.OccurredAtUtc,
                ConsumedAt = request.OccurredAtUtc
            };
        }

        return PublicationGrantAllocationResult.Allocated(snapshot);
    }

    private async Task<PublicationGrantAllocationResult> AllocateRequestedAsync(
        PublicationGrantAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var grantId = request.RequestedGrantId!.Value;
        var resolution = await entitlementResolver.ResolveAsync(
            new EntitlementResolutionContext(
                request.AccountId,
                grantId,
                request.InvitationId,
                request.Action),
            cancellationToken);
        if (!resolution.IsGranted)
        {
            return PublicationGrantAllocationResult.Denied(Map(resolution.Denial));
        }

        var snapshot = resolution.Entitlements!;
        if (snapshot.GrantSource == GrantSource.OrganizationSubscription)
        {
            // One active Organization subscription backs every invitation. The verified resolver
            // supplies the live paid-through check; the shared grant is never assigned,
            // reserved, or consumed per invitation.
            if (snapshot.AssignedInvitationId is not null || snapshot.ReservedAt is not null ||
                snapshot.ConsumedAt is not null)
            {
                return PublicationGrantAllocationResult.Denied(
                    PublicationGrantAllocationDenial.GrantUnavailable);
            }

            return PublicationGrantAllocationResult.Allocated(snapshot);
        }

        var grant = await dbContext.AccountPlanGrants.SingleOrDefaultAsync(
            candidate => candidate.Id == grantId && candidate.AccountId == request.AccountId,
            cancellationToken);
        if (grant is null)
        {
            return PublicationGrantAllocationResult.Denied(
                PublicationGrantAllocationDenial.GrantUnavailable);
        }

        if (request.Action == PublicationEntitlementAction.Schedule)
        {
            grant.ReserveForInvitation(request.InvitationId, request.OccurredAtUtc);
            snapshot = snapshot with
            {
                AssignedInvitationId = request.InvitationId,
                ReservedAt = request.OccurredAtUtc
            };
        }
        else if (request.Action == PublicationEntitlementAction.PublishNow)
        {
            grant.ConsumeForInvitation(request.InvitationId, request.OccurredAtUtc);
            snapshot = snapshot with
            {
                AssignedInvitationId = request.InvitationId,
                ReservedAt = request.OccurredAtUtc,
                ConsumedAt = request.OccurredAtUtc
            };
        }
        else
        {
            throw new InvalidOperationException(
                "Initial publication allocator accepts only PublishNow or Schedule actions.");
        }

        return PublicationGrantAllocationResult.Allocated(snapshot);
    }

    private static void Validate(PublicationGrantAllocationRequest request)
    {
        if (request.AccountId == Guid.Empty || request.InvitationId == Guid.Empty)
        {
            throw new ArgumentException("Publication allocation requires account and invitation ids.");
        }

        if (request.RequestedGrantId == Guid.Empty)
        {
            throw new ArgumentException("Requested grant id must not be empty.", nameof(request));
        }

        if (request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Allocation instant must be UTC.", nameof(request));
        }

        if (request.Action is not PublicationEntitlementAction.PublishNow and
            not PublicationEntitlementAction.Schedule)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported initial publication action.");
        }
    }

    private static PublicationGrantAllocationDenial Map(FreeGrantReservationOutcome outcome) =>
        outcome switch
        {
            FreeGrantReservationOutcome.InvalidConfiguration =>
                PublicationGrantAllocationDenial.InvalidConfiguration,
            FreeGrantReservationOutcome.ReservedForAnotherInvitation =>
                PublicationGrantAllocationDenial.AssignedToAnotherInvitation,
            FreeGrantReservationOutcome.Revoked =>
                PublicationGrantAllocationDenial.GrantNotEffective,
            _ => PublicationGrantAllocationDenial.GrantUnavailable
        };

    private static PublicationGrantAllocationDenial Map(EntitlementResolutionDenial denial) =>
        denial switch
        {
            EntitlementResolutionDenial.GrantNotEffective =>
                PublicationGrantAllocationDenial.GrantNotEffective,
            EntitlementResolutionDenial.AssignedToAnotherInvitation =>
                PublicationGrantAllocationDenial.AssignedToAnotherInvitation,
            EntitlementResolutionDenial.InvalidConfiguration =>
                PublicationGrantAllocationDenial.InvalidConfiguration,
            _ => PublicationGrantAllocationDenial.GrantUnavailable
        };
}
