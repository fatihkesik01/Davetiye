import { InternalLink } from '../../components/ui/InternalLink'

function DraftNotice() {
  return <aside className="auth-card__security-note" role="note">
    <strong>Taslak bilgilendirme.</strong> Bu metinler Phase 11’de yetkin hukuk incelemesinden geçirilecek; şu an hukuki yeterlilik iddiası taşımaz.
  </aside>
}

export function PrivacyInformationPage() {
  return <article className="information-page" aria-labelledby="privacy-heading">
    <DraftNotice />
    <h2 id="privacy-heading">Hizmet bildirimi ve gizlilik bilgisi</h2>
    <p>Kutlio hizmetini sunmak ve hesabınızı yönetmek için gerekli hesap ve davetiye bilgileri işlenir. Bu sayfa, toplanan bilgi türlerini ve ürün içindeki tercihleri anlaşılır biçimde açıklamak için hazırlanmış bir taslaktır.</p>
    <h3>Hizmet için gerekli bilgiler</h3>
    <p>Hesap oluştururken görünen adınız, e-posta adresiniz, hesap türünüz ve oturum açma bilgileri kullanılır. Oluşturduğunuz davetiyelerdeki etkinlik metinleri ve seçtiğiniz özelliklere bağlı olarak RSVP yanıtları, anılar, medya ve hediye rezervasyonları saklanabilir.</p>
    <h3>Hizmet bildirimi ve ürün iletileri</h3>
    <p>Hesap ve davetiye hizmetinin çalışması için gereken hizmet bildirimi, pazarlama izninden ayrıdır ve hesap oluşturma sırasında onaylanır. Ürün haberleri ve kampanyalar için e-posta izni isteğe bağlıdır, varsayılan olarak kapalıdır ve hizmet bildirimini kabul etmeden verilemez.</p>
    <h3>Çerezler ve dış hizmetler</h3>
    <p>Bu sürümde isteğe bağlı analiz veya reklam takibi etkinleştirilmez. Giriş oturumunu sürdürmek gibi temel uygulama işlevleri için gerekli teknik mekanizmalar kullanılabilir. Harita gibi isteğe bağlı dış içerikler açılmadan önce ayrıca gösterilir.</p>
    <h3>Hesap ve içerik talepleri</h3>
    <p>Hesap ve davetiye verilerinin yönetimi ile silme talebinin kapsamı bu üründe ayrıca tanımlanır. Ödeme, güvenlik ve denetim kayıtlarının saklanma süreleri Phase 11 hukuk incelemesinde belirlenecektir; bu fazda bu kayıtlar için otomatik silme uygulanmaz.</p>
    <p>Bu metnin kesin sürümü ve hizmet iletişim bilgileri hukuk incelemesi sonrasında tamamlanacaktır.</p>
    <p><InternalLink to="/kullanim-kosullari">Kullanım koşulları taslağı</InternalLink> · <InternalLink to="/giris/kayit">Hesap oluştur</InternalLink></p>
  </article>
}

export function TermsInformationPage() {
  return <article className="information-page" aria-labelledby="terms-heading">
    <DraftNotice />
    <h2 id="terms-heading">Kullanım koşulları</h2>
    <p>Bu sayfa, Creator ve davetliler için planlanan temel kullanım açıklamalarının taslağıdır. Kesin koşullar Phase 11’de hukuk incelemesinden geçirilecektir.</p>
    <h3>Hesap ve davetiye sahibi</h3>
    <p>Creator, hesabının güvenliğini sağlamaktan ve yayınladığı davetiye bilgilerinin doğruluğunu kontrol etmekten sorumludur. Organizasyon hesabı MVP’de tek hesap sahibi tarafından yönetilir.</p>
    <h3>İçerik ve izinler</h3>
    <p>Creator, davetiye içeriğini ve yüklediği medya dosyalarını kullanma hakkına sahip olmalıdır. Davetliler, Creator’ın açtığı özelliklere göre RSVP yanıtı verebilir, anı veya medya ekleyebilir ve hediye rezervasyonu yapabilir.</p>
    <h3>Hizmetin durumu</h3>
    <p>Yayın, plan ve hesap özellikleri hesap durumuna ve ilgili erişim koşullarına bağlıdır. Ödeme ve yayın süresiyle ilgili güncel bilgi, hesabın plan ve ödeme alanında gösterilir.</p>
    <h3>İnceleme durumu</h3>
    <p>Bu taslak yasal danışmanlık veya tamamlanmış bir sözleşme değildir. Geçerli metin, iletişim kanalı ve yürürlük bilgisi hukuk incelemesinde eklenecektir.</p>
    <p><InternalLink to="/gizlilik">Hizmet bildirimi ve gizlilik bilgisi</InternalLink> · <InternalLink to="/giris/kayit">Hesap oluştur</InternalLink></p>
  </article>
}
