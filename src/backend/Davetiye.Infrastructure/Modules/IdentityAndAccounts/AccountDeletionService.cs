using System.Security.Cryptography;
using System.Text;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>Coordinates email-verified account deletion under the shared account advisory lock.</summary>
public sealed class AccountDeletionService(
    DavetiyeDbContext db,
    IOutermostAccountQuotaTransactionRunner accountLock,
    IOrganizationSubscriptionAccountDeletionCommand subscriptionDeletion,
    IInvitationAccountDeletionCommand invitationDeletion,
    IAccountPlanGrantDeletionCommand grantDeletion,
    IEmailSender emailSender,
    IClock clock,
    IOptions<EmailTokenOptions> tokenOptions,
    IOptions<PublicWebOptions> publicWebOptions) : IAccountDeletionService
{
    public async Task<AccountDeletionRequestOutcome> RequestAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty) return AccountDeletionRequestOutcome.AccountUnavailable;
        return await accountLock.ExecuteAndCommitAsync(accountId, async token =>
        {
            var account = await db.Accounts.SingleOrDefaultAsync(item => item.Id == accountId, token);
            if (account is null || account.DeletionStartedAtUtc is not null)
                return AccountDeletionRequestOutcome.AccountUnavailable;
            var user = await db.Users.SingleOrDefaultAsync(item => item.Id == account.IdentityUserId, token);
            if (user is null || string.IsNullOrWhiteSpace(user.Email) || !user.EmailConfirmed)
                return AccountDeletionRequestOutcome.AccountUnavailable;

            var now = clock.UtcNow.ToUniversalTime();
            var pending = await db.AccountDeletionRequests
                .Where(item => item.AccountId == accountId && item.Status == AccountDeletionRequestStatus.Pending)
                .ToListAsync(token);
            foreach (var prior in pending)
            {
                if (prior.ExpiresAtUtc > now)
                    return AccountDeletionRequestOutcome.Accepted; // Idempotent; do not send repeat mail.
                prior.Expire(now);
            }

            var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
            var expiresAt = now.AddMinutes(Math.Clamp(tokenOptions.Value.TokenLifetimeMinutes, 1, 1440));
            db.AccountDeletionRequests.Add(AccountDeletionRequest.Create(
                Guid.NewGuid(), accountId, tokenHash, now, expiresAt));
            var link = $"{publicWebOptions.Value.BaseUrl.TrimEnd('/')}/hesap-silme/onayla#token={Uri.EscapeDataString(rawToken)}";
            await emailSender.SendShortLivedForAccountAsync(accountId, user.Email,
                EmailNotificationKinds.AccountDeletionConfirmation,
                new Dictionary<string, string> { ["confirmationLink"] = link }, expiresAt, token);
            await db.SaveChangesAsync(token);
            return AccountDeletionRequestOutcome.Accepted;
        }, cancellationToken);
    }

    public async Task<AccountDeletionConfirmationOutcome> ConfirmAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return AccountDeletionConfirmationOutcome.InvalidOrExpiredToken;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var accountId = await db.AccountDeletionRequests.AsNoTracking()
            .Where(item => item.TokenHash == hash && item.Status == AccountDeletionRequestStatus.Pending)
            .Select(item => (Guid?)item.AccountId).SingleOrDefaultAsync(cancellationToken);
        if (accountId is null) return AccountDeletionConfirmationOutcome.InvalidOrExpiredToken;

        return await accountLock.ExecuteAndCommitAsync(accountId.Value, async cancellationToken =>
        {
            // The account advisory lock serializes this transition with checkout, grant, and payment
            // settlement. Lock the token row only after acquiring it to keep lock order consistent.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM account_deletion_requests WHERE token_hash = {hash} FOR UPDATE", cancellationToken);
            var request = await db.AccountDeletionRequests.SingleOrDefaultAsync(
                item => item.TokenHash == hash && item.Status == AccountDeletionRequestStatus.Pending,
                cancellationToken);
            var now = clock.UtcNow.ToUniversalTime();
            if (request is null || !request.Consume(now))
            {
                if (request is not null && request.Expire(now))
                    await db.SaveChangesAsync(cancellationToken);
                return AccountDeletionConfirmationOutcome.InvalidOrExpiredToken;
            }

            var account = await db.Accounts.SingleOrDefaultAsync(item => item.Id == accountId.Value, cancellationToken);
            if (account is null || !account.BeginDeletion(now))
                return AccountDeletionConfirmationOutcome.InvalidOrExpiredToken;

            var work = AccountDeletionWork.Create(Guid.NewGuid(), accountId.Value, now);
            db.AccountDeletionWorks.Add(work);

            await subscriptionDeletion.QueueRenewalCancellationAsync(accountId.Value, now, cancellationToken);
            work.MarkSubscriptionCancellationsQueued(now);

            await invitationDeletion.SchedulePermanentPurgeAsync(accountId.Value, now, cancellationToken);
            work.MarkInvitationsPurgeQueued(now);

            await grantDeletion.RevokeActiveGrantsAsync(accountId.Value, now, cancellationToken);

            var bans = await db.BanRecords.Where(record => record.AccountId == accountId.Value).ToListAsync(cancellationToken);
            foreach (var ban in bans) ban.RedactForAccountDeletion();
            account.AnonymizeForDeletion();

            var user = await db.Users.SingleAsync(item => item.Id == account.IdentityUserId, cancellationToken);
            var syntheticAddress = $"deleted-{user.Id:N}@invalid.local";
            user.UserName = syntheticAddress;
            user.NormalizedUserName = syntheticAddress.ToUpperInvariant();
            user.Email = syntheticAddress;
            user.NormalizedEmail = syntheticAddress.ToUpperInvariant();
            user.EmailConfirmed = false;
            user.PhoneNumber = null;
            user.PhoneNumberConfirmed = false;
            user.TwoFactorEnabled = false;
            user.PasswordHash = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            db.UserClaims.RemoveRange(db.UserClaims.Where(claim => claim.UserId == user.Id));
            db.UserLogins.RemoveRange(db.UserLogins.Where(login => login.UserId == user.Id));
            db.UserTokens.RemoveRange(db.UserTokens.Where(userToken => userToken.UserId == user.Id));
            work.MarkIdentitySanitized(now);
            account.CompleteDeletion(now);
            work.Complete(now);
            await db.SaveChangesAsync(cancellationToken);
            return AccountDeletionConfirmationOutcome.Confirmed;
        }, cancellationToken);
    }

}
