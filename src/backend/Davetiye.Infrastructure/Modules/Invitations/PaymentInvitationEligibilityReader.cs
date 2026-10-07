using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Invitations;

public sealed class PaymentInvitationEligibilityReader(DavetiyeDbContext db) : IPaymentInvitationEligibilityReader
{
    public Task<bool> IsOwnedAndAvailableAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken) =>
        db.Invitations.AsNoTracking().AnyAsync(invitation => invitation.Id == invitationId &&
            invitation.AccountId == accountId && invitation.DeletedAt == null, cancellationToken);
}
