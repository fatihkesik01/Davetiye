using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Templates;
using Davetiye.Domain.Modules.Analytics;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Persistence;

/// <summary>
/// The single DbContext for the whole application (ADR-0001). It extends
/// <see cref="IdentityUserContext{TUser,TKey}"/> (not the full <c>IdentityDbContext</c>) because
/// this milestone's schema needs the ASP.NET Core Identity user/claims/logins/tokens tables for
/// email+password and Google external-login persistence (docs/PRODUCT.md §3), but no accepted
/// product/ADR document describes an ASP.NET Core "Roles" claims table as part of the MVP
/// authorization model — Creator vs. Super Admin is a distinct principal/bootstrap concern
/// (ADR-0002), not a Roles-table concern. A later milestone can add role support if Security
/// decides it is actually needed.
/// </summary>
public class DavetiyeDbContext(DbContextOptions options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<BanRecord> BanRecords => Set<BanRecord>();

    public DbSet<AccountConsentRecord> AccountConsentRecords => Set<AccountConsentRecord>();

    public DbSet<AccountDeletionRequest> AccountDeletionRequests => Set<AccountDeletionRequest>();

    public DbSet<AccountDeletionWork> AccountDeletionWorks => Set<AccountDeletionWork>();

    public DbSet<AdminAuditRecord> AdminAuditRecords => Set<AdminAuditRecord>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();

    public DbSet<AccountPlanGrant> AccountPlanGrants => Set<AccountPlanGrant>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<WorkingContent> WorkingContents => Set<WorkingContent>();

    public DbSet<PublishedContent> PublishedContents => Set<PublishedContent>();

    public DbSet<PublicationWindow> PublicationWindows => Set<PublicationWindow>();

    public DbSet<TemplateDefinition> TemplateDefinitions => Set<TemplateDefinition>();
    public DbSet<InvitationViewTotal> InvitationViewTotals => Set<InvitationViewTotal>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<MediaPlacement> MediaPlacements => Set<MediaPlacement>();

    public DbSet<PendingUpload> PendingUploads => Set<PendingUpload>();

    public DbSet<MediaProviderEvent> MediaProviderEvents => Set<MediaProviderEvent>();

    public DbSet<RsvpConfiguration> RsvpConfigurations => Set<RsvpConfiguration>();

    public DbSet<RsvpQuestion> RsvpQuestions => Set<RsvpQuestion>();

    public DbSet<RsvpQuestionOption> RsvpQuestionOptions => Set<RsvpQuestionOption>();

    public DbSet<RsvpSubmission> RsvpSubmissions => Set<RsvpSubmission>();

    public DbSet<RsvpAnswer> RsvpAnswers => Set<RsvpAnswer>();

    public DbSet<RsvpAnswerOption> RsvpAnswerOptions => Set<RsvpAnswerOption>();

    public DbSet<RsvpManageCapability> RsvpManageCapabilities => Set<RsvpManageCapability>();

    public DbSet<MemoryConfiguration> MemoryConfigurations => Set<MemoryConfiguration>();

    public DbSet<Memory> Memories => Set<Memory>();

    public DbSet<MemoryMedia> MemoryMedia => Set<MemoryMedia>();

    public DbSet<MemoryUploadCapability> MemoryUploadCapabilities => Set<MemoryUploadCapability>();

    public DbSet<GiftItem> GiftItems => Set<GiftItem>();

    public DbSet<GiftReservation> GiftReservations => Set<GiftReservation>();

    public DbSet<GuestGiftSession> GuestGiftSessions => Set<GuestGiftSession>();

    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();

    public DbSet<OrganizationSubscriptionBillingCycle> OrganizationSubscriptionBillingCycles => Set<OrganizationSubscriptionBillingCycle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(DavetiyeDbContext).Assembly,
            type => type.Namespace?.StartsWith(
                "Davetiye.Infrastructure.Modules.",
                StringComparison.Ordinal) == true);

        // Cross-module FK composition belongs to the shared persistence root. Modules exchange
        // primitive invitation IDs through narrow application ports, never foreign entities.
        modelBuilder.Entity<InvitationViewTotal>().HasOne<Invitation>().WithOne()
            .HasForeignKey<InvitationViewTotal>(total => total.InvitationId).OnDelete(DeleteBehavior.Cascade);
        // Invitation owns the Trash overlay. Restricting physical parent deletion forces the purge
        // coordinator to finish provider deletion and remove MediaAsset rows first. Soft Trash does
        // not mutate these rows or delete provider bytes.
        modelBuilder.Entity<MediaAsset>().HasOne<Invitation>().WithMany()
            .HasForeignKey(asset => asset.InvitationId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        modelBuilder.ApplyDavetiyePersistenceConventions();
    }
}
