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
    public const string AccountDeletionConfirmation = "auth.account-deletion-confirmation";

    public const string PurchaseSucceeded = "payment.purchase-succeeded";
    public const string PurchaseFailed = "payment.purchase-failed";
    public const string SubscriptionRenewalSucceeded = "payment.renewal-succeeded";
    public const string SubscriptionRenewalFailed = "payment.renewal-failed";
    public const string SubscriptionCancellation = "payment.subscription-cancelled";
    public const string SubscriptionAccessExpiryReminder = "payment.subscription-access-expiry-reminder";
    public const string PublicationExpiryReminder = "publication.expiry-reminder";
    public const string InvitationPublished = "invitation.published";
}
