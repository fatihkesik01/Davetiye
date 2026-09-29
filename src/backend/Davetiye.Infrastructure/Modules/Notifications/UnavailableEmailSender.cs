using Davetiye.Application.Modules.Notifications.Contracts;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>
/// Registered for <see cref="IEmailSender"/> in every non-Development environment
/// (<see cref="Davetiye.Infrastructure.DependencyInjection.AddInfrastructure"/>). No real
/// Resend-backed adapter exists yet in this phase (docs/ARCHITECTURE.md §5: "Development may use a
/// fake/local sender" implies Production needs a real one), so this type exists to let DI construct
/// callers that merely *depend on* <see cref="IEmailSender"/> - e.g. <c>AuthAccountService</c>, whose
/// Login/Logout/ConfirmEmail methods never actually send an email - without those unrelated code
/// paths breaking. It only fails, loudly and by design, the moment something actually tries to send
/// an email (Register/RequestPasswordReset), mirroring docs/THREAT_MODEL.md §9's FakePaymentGateway
/// production-startup-failure posture for this structurally identical "no real provider adapter yet"
/// case: fail closed rather than silently deliver no email.
///
/// This is deliberately not a throwing DI factory for <see cref="IEmailSender"/> itself: that would
/// make constructing ANY caller that merely takes an <see cref="IEmailSender"/> dependency fail -
/// including Login/Logout/ConfirmEmail, which have nothing to do with email - because minimal API
/// <c>[FromServices]</c> parameters are resolved before endpoint filters (like
/// <c>RequireHttpsInProductionFilter</c>) run.
/// </summary>
public sealed class UnavailableEmailSender : IEmailSender
{
    public Task SendAsync(
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No production IEmailSender adapter is configured. DevEmailSender (docs/ARCHITECTURE.md §5) is a " +
            "development-only fake and must not run outside Development. Implement and register a real " +
            "adapter (e.g. a Resend-backed IEmailSender) before this environment can send email.");
}
