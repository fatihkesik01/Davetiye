using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.Notifications;
using Davetiye.Infrastructure.Persistence;
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

        // Generic inbox/outbox persistence ports (docs/PHASE_0_BASELINE.md §2's "Typed inbox/outbox
        // repository" narrow contract). No real handler or hosted service is registered here yet —
        // that belongs to the business milestone that first produces/consumes a message kind.
        services.AddScoped<IInboxMessageRepository, InboxMessageRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();

        services.AddSingleton<IClock, SystemClock>();

        // M6a: email/password auth foundation. Development uses the fake/local email sender per
        // docs/ARCHITECTURE.md §5; a real Resend-backed adapter is explicitly out of this
        // milestone's scope. Mirroring docs/THREAT_MODEL.md §9's "FakePaymentGateway production'da
        // startup failure üretir" posture for this structurally identical case: DevEmailSender only
        // wires up in Development. Any other environment gets UnavailableEmailSender instead, which
        // fails loudly the moment something actually tries to send an email - see its doc comment
        // for why that failure is deliberately NOT at DI-construction time (a throwing factory here
        // would also break Login/Logout/ConfirmEmail, which depend on IAuthAccountService but never
        // send email, because minimal API [FromServices] binding runs before endpoint filters).
        if (environment.IsDevelopment())
        {
            services.AddScoped<IEmailSender, DevEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, UnavailableEmailSender>();
        }

        services.AddScoped<IAuthAccountService, AuthAccountService>();
        services.AddScoped<IAuthSessionAccessService, AuthSessionAccessService>();
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();
        services.AddScoped<IAdminMfaService, AdminMfaService>();

        services.AddAuthSecurity(configuration, environment);

        return services;
    }
}
