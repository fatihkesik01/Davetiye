using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Templates.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Administration;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Notifications;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Application.Modules.Analytics.Contracts;
using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Application.Modules.Administration;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.Payments;
using Davetiye.Application.Modules.Media;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Modules.Analytics;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Modules.Memories;
using Davetiye.Infrastructure.Modules.GiftRegistry;
using Davetiye.Infrastructure.Modules.Rsvp;
using Davetiye.Infrastructure.Modules.Payments;
using Davetiye.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetRequiredSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DatabaseOptions>>(
            new DatabaseOptionsValidator(environment.IsProduction()));

        services.AddDbContext<DavetiyeDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>()
                .Value;

            options.UseNpgsql(
                databaseOptions.ConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName);
                    npgsql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
                });
        });

        // Generic inbox/outbox persistence ports; business modules own their message handlers.
        services.AddScoped<IInboxMessageRepository, InboxMessageRepository>();
        services.AddScoped<IProviderEventInboxWriter, ProviderEventInboxWriter>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IOutboxWorkStore, OutboxWorkStore>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordResetDestinationRateLimiter, PasswordResetDestinationRateLimiter>();

        // M6 durable email queue. Content is protected before outbox persistence; delivery is
        // provider-specific only behind IEmailTransport. Missing Resend credentials fail closed at
        // delivery time and remain retryable until Phase 11 configures the account.
        services.AddOptions<ResendOptions>().Bind(configuration.GetSection(ResendOptions.SectionName));
        services.AddOptions<EmailOutboxOptions>().Bind(configuration.GetSection(EmailOutboxOptions.SectionName));
        services.AddSingleton<EmailOutboxPayloadProtector>();
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddScoped<IEmailOwnedDispatchGate, AccountOwnedEmailDispatchGate>();
        services.AddSingleton<EmailTemplateRenderer>();
        if (environment.IsDevelopment())
        {
            services.AddScoped<IEmailTransport, DevEmailTransport>();
        }
        else
        {
            services.AddHttpClient<ResendEmailTransport>()
                .ConfigurePrimaryHttpMessageHandler(ResendEmailTransport.CreatePrimaryHandler);
            services.AddScoped<IEmailTransport>(sp => sp.GetRequiredService<ResendEmailTransport>());
        }
        services.AddHostedService<EmailOutboxWorker>();

        services.AddScoped<IAuthAccountService, AuthAccountService>();
        services.AddScoped<IAccountConsentService, AccountConsentService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IOrganizationSubscriptionAccountDeletionCommand, OrganizationSubscriptionAccountDeletionHandler>();
        services.AddScoped<IInvitationAccountDeletionCommand, InvitationAccountDeletionHandler>();
        services.AddScoped<IAccountPlanGrantDeletionCommand, AccountPlanGrantDeletionHandler>();
        services.AddScoped<IAccountDeletionStatusReader, AccountDeletionStatusReader>();
        services.AddScoped<IAdminAccountOverviewReader, AdminAccountOverviewReader>();
        services.AddScoped<IAdminBannedAccountListReader, AdminBannedAccountListReader>();
        services.AddScoped<IAdminInvitationOverviewReader, AdminInvitationOverviewReader>();
        services.AddScoped<IAdminPlanAndGrantOverviewReader, AdminPlanAndGrantOverviewReader>();
        services.AddScoped<IAdminPaymentOverviewReader, AdminPaymentOverviewReader>();
        services.AddScoped<IAdminPaymentListReader, AdminPaymentListReader>();
        services.AddScoped<IAdminMediaOverviewReader, AdminMediaOverviewReader>();
        services.AddScoped<IAdminOverviewHealthReader, AdminOverviewHealthReader>();
        services.AddScoped<IAdminOverviewService, AdminOverviewService>();
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        services.AddScoped<IAdminAuditListReader, AdminAuditListReader>();
        services.AddScoped<IAdminBanService, AdminBanService>();
        services.AddScoped<IAuthSessionAccessService, AuthSessionAccessService>();
        services.AddScoped<ICurrentAccountResolver, CurrentAccountResolver>();
        services.AddScoped<IAccountReferenceValidator, AccountReferenceValidator>();
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();
        services.AddScoped<IAdminMfaService, AdminMfaService>();
        services.AddScoped<IInvitationDraftService, InvitationDraftService>();
        services.AddScoped<IRsvpCreatorInvitationAccessReader, RsvpCreatorInvitationAccessReader>();
        services.AddScoped<ICreatorMediaInvitationAccessReader, CreatorMediaInvitationAccessReader>();
        services.AddScoped<ICreatorMediaInvitationOwnerReader, CreatorMediaInvitationOwnerReader>();
        services.AddScoped<IMediaPublicationSnapshotReader, MediaPublicationSnapshotReader>();
        services.AddSingleton<IPublicCodeGenerator, CryptographicPublicCodeGenerator>();
        services.AddScoped<IInitialPublicationStore, InitialPublicationStore>();
        services.AddScoped<IInitialPublicationPreflightValidator, InitialPublicationPreflightValidator>();
        services.AddSingleton<IIanaTimeZoneValidator, IanaTimeZoneValidator>();
        services.AddScoped<IInitialPublicationService, InitialPublicationService>();
        services.AddScoped<IPublicationLifecycleService, PublicationLifecycleService>();
        services.AddScoped<IInvitationTrashService, InvitationTrashService>();
        services.AddScoped<IInvitationLifecycleJobs, InvitationLifecycleJobs>();
        services.AddScoped<IInvitationViewCounter, InvitationViewCounter>();
        services.AddScoped<IInvitationStatisticsReader, InvitationStatisticsReader>();
        services.AddScoped<ICreatorMediaIntentStore, CreatorMediaIntentStore>();
        services.AddScoped<IMediaPurgeCoordinator, MediaPurgeCoordinator>();
        services.AddScoped<IRsvpPurgeCoordinator, RsvpPurgeCoordinator>();
        services.AddScoped<ICreatorRsvpConfigurationService, CreatorRsvpConfigurationService>();
        services.AddScoped<ICreatorRsvpResultsService, CreatorRsvpResultsService>();
        services.AddScoped<IPublicRsvpService, PublicRsvpService>();
        services.AddScoped<IRsvpGuestInvitationAccessReader, RsvpGuestInvitationAccessReader>();
        services.AddOptions<RsvpCapabilityOptions>().Bind(configuration.GetSection(RsvpCapabilityOptions.SectionName));
        services.AddOptions<RsvpInputLimits>()
            .Bind(configuration.GetSection(RsvpInputLimits.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RsvpInputLimits>, RsvpInputLimitsValidator>();
        services.AddScoped<IMemoriesPurgeCoordinator, MemoriesPurgeCoordinator>();
        services.AddScoped<IGiftRegistryPurgeCoordinator, GiftRegistryPurgeCoordinator>();
        services.AddScoped<ICreatorGiftRegistryService, CreatorGiftRegistryService>();
        services.AddScoped<IGiftCreatorInvitationAccessReader, GiftCreatorInvitationAccessReader>();
        services.AddScoped<IGiftGuestInvitationAccessReader, GiftGuestInvitationAccessReader>();
        services.AddScoped<IPublicGiftRegistryService, PublicGiftRegistryService>();
        services.AddOptions<GiftCapabilityOptions>().Bind(configuration.GetSection(GiftCapabilityOptions.SectionName));
        services.AddScoped<IMemoriesGuestInvitationAccessReader, MemoriesGuestInvitationAccessReader>();
        services.AddScoped<IMemoriesCreatorInvitationAccessReader, MemoriesCreatorInvitationAccessReader>();
        services.AddScoped<IPublicMemoriesService, PublicMemoriesService>();
        services.AddScoped<ICreatorMemoryConfigurationService, CreatorMemoryConfigurationService>();
        services.AddScoped<ICreatorMemoriesService, CreatorMemoriesService>();
        services.AddSingleton<ICreatorMemoriesRateLimiter, CreatorMemoriesRateLimiter>();
        services.AddSingleton<IPublicMemorySubmissionLimiter, PublicMemorySubmissionLimiter>();
        services.AddSingleton<IPublicMemoryUploadIntentLimiter, PublicMemoryUploadIntentLimiter>();
        services.AddScoped<IPublicMemoryUploadService, PublicMemoryUploadService>();
        services.AddScoped<IMemoryUploadExpirySweeper, MemoryUploadExpirySweeper>();
        services.AddOptions<MemoryUploadCapabilityOptions>().Bind(configuration.GetSection(MemoryUploadCapabilityOptions.SectionName));
        services.AddScoped<GuestMediaStore>();
        services.AddScoped<IGuestMediaStore>(provider => provider.GetRequiredService<GuestMediaStore>());
        services.AddScoped<IGuestMediaAssetStatusReader>(provider => provider.GetRequiredService<GuestMediaStore>());
        services.AddScoped<IGuestMediaUploadService, GuestMediaUploadService>();
        services.AddSingleton<IGuestMediaUploadAvailability, GuestMediaUploadAvailability>();
        services.AddOptions<MemoryInputLimits>()
            .Bind(configuration.GetSection(MemoryInputLimits.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MemoryInputLimits>, MemoryInputLimitsValidator>();
        services.AddScoped<IMediaLifecycleJobs, MediaLifecycleJobs>();
        services.AddSingleton<IMediaLifecycleMetrics, MediaLifecycleMetrics>();
        services.AddSingleton<MediaReconciliationCursor>();
        services.AddScoped<IMediaProviderAssetMaintenance>(provider => provider.GetRequiredService<CloudflareMediaAssetLifecycleGateway>());
        services.AddScoped<ICreatorMediaIntentService, CreatorMediaIntentService>();
        services.AddScoped<ICreatorMediaLibraryService, CreatorMediaLibraryService>();
        services.AddScoped<IPublicMediaDeliveryService, PublicMediaDeliveryService>();
        services.AddScoped<IGuestMemoryMediaDeliveryService, GuestMemoryMediaDeliveryService>();
        services.AddScoped<IMediaVerificationStore, MediaVerificationStore>();
        services.AddScoped<MediaVerificationService>();
        services.AddScoped<IMediaVerificationService>(provider => provider.GetRequiredService<MediaVerificationService>());
        services.AddScoped<IGuestMediaVerificationService>(provider => provider.GetRequiredService<MediaVerificationService>());
        services.AddSingleton<IStreamWebhookSignatureVerifier, StreamWebhookSignatureVerifier>();
        services.AddSingleton<ICreatorMediaIntentRateLimiter, CreatorMediaIntentRateLimiter>();
        services.AddSingleton<ICreatorRsvpRateLimiter, CreatorRsvpRateLimiter>();
        services.AddScoped<UnavailableMediaUploadAdapter>();
        services.AddOptions<CloudflareMediaOptions>()
            .Bind(configuration.GetSection(CloudflareMediaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CloudflareMediaOptions>, CloudflareMediaOptionsValidator>();
        services.AddSingleton<IMediaContentSources, CloudflareMediaContentSources>();
        services.AddHttpClient<CloudflareMediaUploadAdapter>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<CloudflarePrivateMediaDeliveryGateway>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<CloudflareMediaAssetLifecycleGateway>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IPrivateMediaDeliveryGateway>(provider => provider.GetRequiredService<CloudflarePrivateMediaDeliveryGateway>());
        services.AddScoped<IMediaUploadGateway>(provider => provider.GetRequiredService<IOptions<CloudflareMediaOptions>>().Value.Enabled
            ? provider.GetRequiredService<CloudflareMediaUploadAdapter>()
            : provider.GetRequiredService<UnavailableMediaUploadAdapter>());
        services.AddScoped<IMediaImageNormalizationPipeline>(provider => provider.GetRequiredService<IOptions<CloudflareMediaOptions>>().Value.Enabled
            ? provider.GetRequiredService<CloudflareMediaUploadAdapter>()
            : provider.GetRequiredService<UnavailableMediaUploadAdapter>());
        services.AddScoped<IPublicInvitationService, PublicInvitationService>();
        services.AddScoped<IInvitationOwnershipValidator, InvitationOwnershipValidator>();
        services.AddScoped<IPaymentCheckoutService, PaymentCheckoutService>();
        services.Configure<IyzicoWebhookOptions>(configuration.GetSection(IyzicoWebhookOptions.SectionName));
        services.Configure<IyzicoPaymentApiOptions>(configuration.GetSection(IyzicoPaymentApiOptions.SectionName));
        services.Configure<PaymentWebhookProcessingOptions>(configuration.GetSection(PaymentWebhookProcessingOptions.SectionName));
        services.AddScoped<IPaymentWebhookIngestor, IyzicoPaymentWebhookIngestor>();
        services.AddScoped<IPaymentWebhookProcessingStore, PaymentWebhookProcessingStore>();
        services.AddScoped<PaymentWebhookProcessor>();
        services.AddHostedService<PaymentWebhookWorker>();
        services.AddScoped<IPaymentAccountEligibilityReader, PaymentAccountEligibilityReader>();
        services.AddScoped<IPaymentInvitationEligibilityReader, PaymentInvitationEligibilityReader>();
        services.AddScoped<IPaymentPlanCatalogReader, PaymentPlanCatalogReader>();
        services.AddScoped<IPublicPlanCatalogReader, PublicPlanCatalogReader>();
        var fakePaymentBaseUrl = configuration["Payments:FakeCheckoutBaseUrl"] ?? "http://localhost:5173/fake-checkout/";
        if (environment.IsDevelopment())
        {
            var fakeGateway = new FakePaymentGateway(fakePaymentBaseUrl);
            services.AddSingleton<IPaymentGateway>(fakeGateway);
            services.AddSingleton<IPaymentResultVerifier>(fakeGateway);
        }
        else
        {
            // No real merchant adapter is enabled before the Phase 11 provider acceptance gate.
            // In particular, the development FakePaymentGateway is never registered outside Development.
            services.AddSingleton<IPaymentGateway, UnavailablePaymentGateway>();
            services.AddHttpClient<IyzicoPaymentResultVerifier>()
                .ConfigurePrimaryHttpMessageHandler(IyzicoPaymentResultVerifier.CreatePrimaryHandler);
            services.AddScoped<IPaymentResultVerifier>(provider =>
                provider.GetRequiredService<IyzicoPaymentResultVerifier>());
        }
        services.AddScoped<ITemplateSelectionResolver, TemplateSelectionResolver>();
        services.AddScoped<ITemplateModuleSupportReader, TemplateModuleSupportReader>();
        services.AddSingleton<ITemplateRendererRegistry, TemplateRendererRegistry>();
        services.AddScoped<ITemplateCatalogService, TemplateCatalogService>();
        services.AddScoped<IAdminTemplateService, AdminTemplateService>();
        services.AddScoped<IAdminPlanService, AdminPlanService>();
        services.AddScoped<IAdminSystemSettingsService, AdminSystemSettingsService>();
        services.AddScoped<TemplateCatalogInitializer>();
        services.AddScoped<PlanCatalogInitializer>();
        services.AddScoped<InvitationRetentionSettingsInitializer>();
        services.AddScoped<IInvitationRetentionSettingsReader, InvitationRetentionSettingsReader>();
        services.AddScoped<AbandonedMemoryRetentionSettingsInitializer>();
        services.AddScoped<IAbandonedMemoryRetentionSettingsReader, AbandonedMemoryRetentionSettingsReader>();
        services.AddScoped<IEntitlementGrantReader, EntitlementGrantReader>();
        services.AddScoped<IOrganizationSubscriptionEntitlementReader, OrganizationSubscriptionEntitlementReader>();
        services.AddScoped<IOrganizationSubscriptionLifecycleStore, OrganizationSubscriptionLifecycleStore>();
        services.AddScoped<IOrganizationSubscriptionLifecycleJobs>(provider =>
            (IOrganizationSubscriptionLifecycleJobs)provider.GetRequiredService<IOrganizationSubscriptionLifecycleStore>());
        services.AddScoped<IOrganizationSubscriptionLifecycleService, OrganizationSubscriptionLifecycleService>();
        services.AddScoped<AccountQuotaTransactionRunner>();
        services.AddScoped<IAccountQuotaTransactionRunner>(provider =>
            provider.GetRequiredService<AccountQuotaTransactionRunner>());
        services.AddScoped<IOutermostAccountQuotaTransactionRunner>(provider =>
            provider.GetRequiredService<AccountQuotaTransactionRunner>());
        services.AddScoped<IFreeGrantReservationStore, FreeGrantReservationStore>();
        services.AddScoped<IEffectiveEntitlementResolver, EffectiveEntitlementResolver>();
        services.AddScoped<IFreePublicationGrantService, FreePublicationGrantService>();
        services.AddScoped<IPublicationGrantAllocator, PublicationGrantAllocator>();
        services.AddScoped<IPublicationGrantLifecycleService, PublicationGrantLifecycleService>();
        services.AddScoped<IPublicationGrantAccessValidator, PublicationGrantAccessValidator>();

        services.AddAuthSecurity(configuration, environment);

        return services;
    }

}
