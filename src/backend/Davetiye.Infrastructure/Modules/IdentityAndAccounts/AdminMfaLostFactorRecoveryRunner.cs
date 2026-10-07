using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public enum AdminMfaLostFactorRecoveryOutcome
{
    Recovered,
    RefusedInvalidOperator,
    RefusedNotSuperAdmin,
    RefusedCreatorAccount,
}

/// <summary>
/// Trusted-server operation for an independently verified platform owner to recover a locked-out
/// Super Admin. This intentionally has no HTTP endpoint or application-level authorization path.
/// </summary>
public sealed class AdminMfaLostFactorRecoveryRunner(
    UserManager<ApplicationUser> userManager,
    IUserStore<ApplicationUser> userStore,
    DavetiyeDbContext dbContext,
    IAdminAuditWriter audit)
{
    public async Task<AdminMfaLostFactorRecoveryOutcome> RunAsync(
        Guid targetIdentityUserId,
        Guid platformOperatorId,
        bool confirmed,
        bool outOfBandIdentityVerified,
        CancellationToken cancellationToken)
    {
        if (targetIdentityUserId == Guid.Empty)
            throw new ArgumentException("A target identity user ID is required.", nameof(targetIdentityUserId));
        if (platformOperatorId == Guid.Empty)
            throw new ArgumentException("A platform operator ID is required.", nameof(platformOperatorId));
        if (!confirmed)
            throw new InvalidOperationException("Explicit --confirm acknowledgment is required.");
        if (!outOfBandIdentityVerified)
            throw new InvalidOperationException("Out-of-band identity verification acknowledgment is required.");

        var user = await userManager.FindByIdAsync(targetIdentityUserId.ToString());
        if (user is null)
            throw new InvalidOperationException("The target Identity user does not exist.");

        var operatorUser = await userManager.FindByIdAsync(platformOperatorId.ToString());
        if (operatorUser is null ||
            await dbContext.Accounts.AnyAsync(account => account.IdentityUserId == operatorUser.Id, cancellationToken))
            return AdminMfaLostFactorRecoveryOutcome.RefusedInvalidOperator;

        var operatorClaims = await userManager.GetClaimsAsync(operatorUser);
        if (!operatorClaims.Any(claim => claim.Type == SuperAdminClaimNames.SuperAdmin &&
                                         claim.Value == SuperAdminClaimNames.SuperAdminClaimValue))
            return AdminMfaLostFactorRecoveryOutcome.RefusedInvalidOperator;

        var claims = await userManager.GetClaimsAsync(user);
        if (!claims.Any(claim => claim.Type == SuperAdminClaimNames.SuperAdmin &&
                                 claim.Value == SuperAdminClaimNames.SuperAdminClaimValue))
            return AdminMfaLostFactorRecoveryOutcome.RefusedNotSuperAdmin;

        if (await dbContext.Accounts.AnyAsync(
                account => account.IdentityUserId == user.Id, cancellationToken))
            return AdminMfaLostFactorRecoveryOutcome.RefusedCreatorAccount;

        // Identity's EF store participates in this transaction. The generated replacement key is
        // never returned or logged; enrollment will issue a new key after the recovered user signs in.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var disableResult = await userManager.SetTwoFactorEnabledAsync(user, false);
        EnsureSucceeded(disableResult, "disable the old MFA factor");

        var resetResult = await userManager.ResetAuthenticatorKeyAsync(user);
        EnsureSucceeded(resetResult, "replace the lost authenticator key");

        if (userStore is not IUserTwoFactorRecoveryCodeStore<ApplicationUser> recoveryCodeStore)
            throw new InvalidOperationException("The configured Identity store cannot clear recovery codes.");

        await recoveryCodeStore.ReplaceCodesAsync(user, Array.Empty<string>(), cancellationToken);

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stampResult, "revoke existing authentication sessions");

        audit.Add(platformOperatorId, DateTimeOffset.UtcNow,
            "SuperAdminMfaLostFactorRecovered", user.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AdminMfaLostFactorRecoveryOutcome.Recovered;
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {operation}: " +
                string.Join(" ", result.Errors.Select(error => error.Code)));
    }
}
