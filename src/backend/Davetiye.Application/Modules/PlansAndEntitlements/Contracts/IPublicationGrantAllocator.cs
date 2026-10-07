namespace Davetiye.Application.Modules.PlansAndEntitlements.Contracts;

/// <summary>
/// Plans-owned narrow mutation boundary for an initial publication. It is called only from inside
/// the account quota transaction. A null RequestedGrantId explicitly selects the lifetime Free
/// path; an invalid supplied grant never falls back to Free.
/// </summary>
public interface IPublicationGrantAllocator
{
    /// <summary>
    /// Returns a post-allocation snapshot. For individual/Free sources the snapshot is assigned to
    /// the requested invitation and is reserved (Schedule) or consumed (PublishNow). A denial must
    /// stage no mutation. The caller rolls the transaction back after any later business denial.
    /// </summary>
    Task<PublicationGrantAllocationResult> AllocateAsync(
        PublicationGrantAllocationRequest request,
        CancellationToken cancellationToken);
}

public sealed record PublicationGrantAllocationRequest(
    Guid AccountId,
    Guid InvitationId,
    Guid? RequestedGrantId,
    PublicationEntitlementAction Action,
    DateTimeOffset OccurredAtUtc);

public enum PublicationGrantAllocationDenial
{
    None,
    GrantUnavailable,
    GrantNotEffective,
    AssignedToAnotherInvitation,
    InvalidConfiguration
}

public sealed record PublicationGrantAllocationResult(
    EffectiveEntitlementSnapshot? Entitlements,
    PublicationGrantAllocationDenial Denial)
{
    public bool IsAllocated =>
        Entitlements is not null && Denial == PublicationGrantAllocationDenial.None;

    public static PublicationGrantAllocationResult Allocated(
        EffectiveEntitlementSnapshot entitlements) =>
        new(entitlements, PublicationGrantAllocationDenial.None);

    public static PublicationGrantAllocationResult Denied(
        PublicationGrantAllocationDenial denial) => new(null, denial);
}
