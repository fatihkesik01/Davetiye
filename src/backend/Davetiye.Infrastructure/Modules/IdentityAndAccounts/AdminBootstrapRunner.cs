using System.Security.Claims;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

public enum AdminBootstrapOutcome
{
    /// <summary>A new Identity user was created and granted the Super Admin claim.</summary>
    Created,

    /// <summary>An existing Identity user was granted the Super Admin claim.</summary>
    Granted,

    /// <summary>The target user already held the Super Admin claim; nothing changed.</summary>
    AlreadySuperAdmin,

    /// <summary>
    /// Refused: the target Identity user already has a Domain <c>Account</c> (a Creator). Per
    /// docs/adr/0002 and docs/PHASE_0_PLAN.md §3, "Kontrollü bootstrap edilmiş Super Admin
    /// principal'ı Creator hesabı taşımaz" — a Super Admin principal must never carry a Creator
    /// Account. The Super Admin claim was NOT granted.
    /// </summary>
    RefusedExistingCreatorAccount,
}

/// <summary>
/// The testable core of the one-time Admin bootstrap operation (docs/adr/0002: "Production Admin
/// bootstrap public HTTP endpoint değil, tek seferlik kontrollü operasyon/CLI'dır"), split out of
/// <c>tools/Davetiye.AdminBootstrap</c>'s <c>Program.cs</c> the same way
/// <see cref="Davetiye.Infrastructure.Persistence.DatabaseMigrationRunner"/> is split out of
/// <c>tools/Davetiye.DatabaseMigrator</c>'s — so integration tests can exercise the real logic
/// in-process without spawning the executable as a subprocess.
///
/// Never creates a Domain <c>Account</c> for the bootstrapped user (Super Admin has no Account, per
/// docs/PHASE_0_PLAN.md §3's "IdentityUser 0..1 Account") and is idempotent: re-running it for the
/// same email neither duplicates the Identity user nor errors — it reports
/// <see cref="AdminBootstrapOutcome.AlreadySuperAdmin"/> and leaves everything else untouched.
/// </summary>
public sealed class AdminBootstrapRunner(UserManager<ApplicationUser> userManager, DavetiyeDbContext dbContext)
{
    public async Task<AdminBootstrapOutcome> RunAsync(
        string email, string? password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var user = await userManager.FindByEmailAsync(email);
        var created = false;

        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"No Identity user exists for '{email}'. A password is required to create one.");
            }

            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                // A Super Admin must be able to log in through the same
                // IdentityOptions.SignIn.RequireConfirmedAccount gate every other account does
                // (SecurityServiceCollectionExtensions); there is no separate confirmation-email flow
                // for this bootstrap path, so email is confirmed directly.
                EmailConfirmed = true,
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to create the Super Admin identity user: " +
                    string.Join(" ", createResult.Errors.Select(error => error.Description)));
            }

            created = true;
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var confirmResult = await userManager.UpdateAsync(user);
            if (!confirmResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to confirm the existing account's email: " +
                    string.Join(" ", confirmResult.Errors.Select(error => error.Description)));
            }
        }

        var existingClaims = await userManager.GetClaimsAsync(user);
        var alreadySuperAdmin = existingClaims.Any(claim =>
            claim.Type == SuperAdminClaimNames.SuperAdmin &&
            claim.Value == SuperAdminClaimNames.SuperAdminClaimValue);

        if (alreadySuperAdmin)
        {
            return AdminBootstrapOutcome.AlreadySuperAdmin;
        }

        // ADR-0002 / docs/PHASE_0_PLAN.md §3 invariant: a Super Admin principal must not carry a
        // Creator Account. A brand-new user created above can never already have one (its Account, if
        // any, would have to reference this same freshly generated Identity user id, which did not
        // exist until this call) — this only ever fires for a pre-existing Identity user who already
        // registered as a Creator. Refuse rather than silently proceeding: this is a trusted-operator
        // console tool, so a clear rejection is sufficient without deeper remediation tooling.
        var hasAccount = await dbContext.Accounts.AnyAsync(
            account => account.IdentityUserId == user.Id, cancellationToken);
        if (hasAccount)
        {
            return AdminBootstrapOutcome.RefusedExistingCreatorAccount;
        }

        var addClaimResult = await userManager.AddClaimAsync(
            user, new Claim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue));

        if (!addClaimResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to grant the Super Admin claim: " +
                string.Join(" ", addClaimResult.Errors.Select(error => error.Description)));
        }

        return created ? AdminBootstrapOutcome.Created : AdminBootstrapOutcome.Granted;
    }
}
