namespace Davetiye.Application.Modules.Notifications.Contracts;

/// <summary>
/// The closed set of message-kind discriminators <see cref="IEmailSender"/> understands in this
/// milestone. Kept as plain string constants (not an enum) so a future module can add its own kind
/// without a shared enum becoming a cross-module bottleneck.
/// </summary>
public static class EmailNotificationKinds
{
    public const string EmailConfirmation = "auth.email-confirmation";

    public const string PasswordReset = "auth.password-reset";
}
