using System.Text.Encodings.Web;
using Davetiye.Application.Modules.Notifications.Contracts;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>Renders the finite set of accepted transactional messages; payload values are always escaped.</summary>
public sealed class EmailTemplateRenderer
{
    public EmailDeliveryMessage Render(string toEmail, string kind, IReadOnlyDictionary<string, string> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(data);

        var (subject, text) = kind switch
        {
            EmailNotificationKinds.EmailConfirmation => ("E-posta adresinizi doğrulayın", $"Merhaba {Get(data, "displayName")}, hesabınızı doğrulamak için bağlantıyı açın: {Get(data, "confirmationLink")}"),
            EmailNotificationKinds.PasswordReset => ("Şifre yenileme bağlantınız", $"Şifrenizi yenilemek için bağlantıyı açın: {Get(data, "resetLink")}"),
            EmailNotificationKinds.AccountDeletionConfirmation => ("Hesap silme talebinizi doğrulayın", $"Hesabınızın kalıcı olarak silinmesi için bu bağlantıyı açıp talebinizi doğrulayın: {Get(data, "confirmationLink")}"),
            EmailNotificationKinds.PurchaseSucceeded => ("Satın alma onayı", $"{Get(data, "planName")} davetiye paketiniz etkinleştirildi."),
            EmailNotificationKinds.PurchaseFailed => ("Ödeme tamamlanamadı", "Ödemeniz tamamlanamadı. Hesabınızdan yeni bir ödeme denemesi başlatabilirsiniz."),
            EmailNotificationKinds.SubscriptionRenewalSucceeded => ("Abonelik yenilendi", "Organization aboneliğinizin aylık ödemesi başarıyla alındı."),
            EmailNotificationKinds.SubscriptionRenewalFailed => ("Abonelik ödemesi bekleniyor", "Aylık ödeme alınamadı. Ödeme sağlayıcısı otomatik olarak yeniden deneyecek."),
            EmailNotificationKinds.SubscriptionCancellation => ("Abonelik iptal edildi", $"Aboneliğiniz {Get(data, "accessEndDate")} tarihinde sona erecek."),
            EmailNotificationKinds.SubscriptionAccessExpiryReminder => ("Organization erişiminiz 7 gün içinde sona erecek", $"İptal ettiğiniz Organization aboneliğinizin mevcut paid-through erişimi 7 gün içinde, {Get(data, "accessEndDate")} tarihinde sona erecek."),
            EmailNotificationKinds.PublicationExpiryReminder => ("Yayın süreniz yakında bitiyor", $"{Get(data, "invitationTitle")} davetiyenizin yayın süresi 7 gün içinde sona erecek."),
            EmailNotificationKinds.InvitationPublished => ("Davetiyeniz yayınlandı", $"{Get(data, "invitationTitle")} davetiyeniz artık yayında."),
            _ => throw new InvalidOperationException("Unsupported transactional email kind.")
        };

        var html = $"<!doctype html><html lang=\"tr\"><body><main><h1>{HtmlEncoder.Default.Encode(subject)}</h1><p>{HtmlEncoder.Default.Encode(text)}</p></main></body></html>";
        return new EmailDeliveryMessage(toEmail, subject, html, text);
    }

    private static string Get(IReadOnlyDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && value.Length <= 16_384 ? value : throw new InvalidOperationException("Email template data is incomplete.");
}
