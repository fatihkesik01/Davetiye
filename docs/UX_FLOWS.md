# Faz 0 — MVP Kullanıcı Yolculukları ve Ekran Sınırları

Durum: **Accepted baseline**  
Tarih: **2026-09-28**

Bu belge business feature tasarımı değildir; PRODUCT.md kapsamındaki
Creator, Guest ve Super Admin deneyimlerinin implementation sınırını
belirler.

## 1. Route ve Shell Sınırları

### Public shell

- `/sablonlar`: public template katalog
- Template demo preview: kesin path implementation contract aşamasında
  belirlenir
- `/davetiye/:slug-:publicCode`: Active public invitation veya PII-free
  inactive state
- Kayıt, giriş, email verification ve password reset rotaları

Public invitation Creator/Admin navigasyonu taşımaz. Public sayfada ayrı
paylaş butonu bulunmaz.

### Creator shell

- `/panel`: özet
- `/panel/davetiyeler`: invitation listesi ve durum eylemleri
- `/panel/davetiyeler/yeni/*`: adımlı creation
- `/panel/davetiyeler/:id/*`: edit, module, preview, publish/share ve
  sonuç yönetimi
- `/panel/cop-kutusu`
- `/panel/plan-odeme`
- `/panel/hesap-guvenlik`

Path adları kontrat kesinleşirken değişebilir; authorization ve ekran
sınırları değişmez.

### Super Admin shell

- `/admin`
- Kullanıcılar/ban
- Planlar/entitlements
- Ödemeler
- Depolama/sistem sağlığı
- Audit

Admin shell Creator'dan görsel ve yetkisel olarak ayrıdır. Özel RSVP,
memory ve gift guest içeriği normal Admin navigasyonunda bulunmaz.

## 2. Creator Ana Yolculuğu

1. Creator public template katalogdan veya panelden creation başlatır.
2. Gerekirse kayıt/giriş ve email verification tamamlanır.
3. Etkinlik türü, temel bilgi, template ve tarih/mekân girilir.
4. Galeri, program, RSVP, memories ve gift gibi opsiyonel adımlar
   **Şimdilik Geç** ile atlanabilir.
5. Anlamlı değişiklikler backend WorkingContent'e autosave edilir.
6. Creator telefon/tablet/desktop preview veya yeni sekme preview açar.
7. Publish preflight required blocker ve recommended warning'leri ayırır.
8. Creator hemen publish veya planlı başlangıç seçer; opsiyonel bitiş
   paket window'u dışına çıkamaz.
9. Publish sonrası share, yönetim ve aggregate stats alanları açılır.
10. Active düzenlemede autosave değişiklikleri “henüz yayında değil”
    olarak ayrılır; explicit **Güncelle** PublishedContent'i yeniler.

Wizard'ın kesin ekran sırası açık decision register konusudur. Domain ve
API adım sırasına bağımlı tasarlanmaz.

## 3. Autosave, Skip ve Preview

Autosave UI durumları:

- Kaydediliyor
- Kaydedildi
- Bağlantı yok — cihazda geçici korunuyor
- Kaydetme başarısız — tekrar dene
- Sunucudaki revision değişti — conflict çözümü gerekli

Kurallar:

- Browser storage yalnız bağlantı kurtarmasıdır, source of truth değildir.
- Yalnız gerçekten kaydedilmemiş veri varsa sayfadan ayrılma uyarısı
  gösterilir.
- Skip yalnız opsiyonel adımda görünür; skip edilen modül kapalıdır ve
  panelden sonradan açılabilir.
- Paket nedeniyle kapalı feature, “skip edilmiş” gibi gösterilmez.
- Preview ve public aynı renderer/render modelini kullanır.
- Cihaz seçimi yalnız viewport'u değiştirir.
- Draft preview public erişim oluşturmaz.
- Preview RSVP, memory, gift, view-stat veya media mutation üretmez;
  etkileşimler yalnız güvenli UI simülasyonudur.

## 4. Lifecycle Ekran Davranışı

| State | Creator ana eylemleri | Public |
| --- | --- | --- |
| Draft | Edit, preview, publish/schedule, delete | 404/erişilemez |
| Scheduled | Başlangıç öncesi preview/zaman; window başlayınca Active action set'i ve template lock; edit semantiği PD-13'e bağlı | Başlamadan önce PII-free unavailable; window başlayınca effective Active |
| Active | Edit working content, update, share, pause, sonuç yönetimi | Full invitation |
| Paused | Edit, template değiştir, preview, window uygunsa resume | PII-free unavailable |
| Expired | İçerik/sonuç gör, uygun yeni hak al, karar verilmiş biçimde reactivate | PII-free unavailable |
| Trash | Restore; retention kalan süre | 404 |
| Purged | Erişilemez | 404 |

- Pause confirmation sürenin durmayacağını ve `endsAt` değişmeyeceğini
  açıklar.
- Active template seçimi disabled açıklamasız bırakılmaz; pause akışı
  anlatılır.
- Expired reactivation aynı public URL'nin korunacağını belirtir.
- Başlangıcı gelmemiş Scheduled ile Paused/Expired/banned owner aynı
  private-data-free public shell'i görür; uygun window başladığında Scheduled
  kayıt effective Active olarak full invitation gösterir.
- Delete confirmation public erişimin hemen kapanacağını ve trash
  retention'ı açıklar.

## 5. Guest Yolculukları

### Public invitation

1. Guest paylaşılan URL'yi açar.
2. Active/effective ise allowlisted invitation projection render edilir.
3. Tarih, mekân, interaktif harita/yönlendirme ve takvime ekleme
   kullanılabilir.
4. Creator'ın açtığı RSVP, Memories ve Gift modülleri görünür.
5. Guest hesabı istenmez.

### RSVP

1. Guest etkin soruları görür.
2. Required/validation alan yanında ve submit özetinde açıklanır.
3. Başarılı submission sonrası manage capability cookie oluşturulur.
4. Aynı browser “Daha önce yanıt verdiniz / Yanıtımı Güncelle” akışını
   kullanabilir.
5. Capability yoksa önceki cevap aranmaz; duplicate mümkün olabilir.
6. Başka guest cevapları veya aggregate sonuçlar gösterilmez.

Creator görünümünde submission listesi ile aggregate toplamlar ayrılır.
Semantic participant-count sorusu değiştirildiğinde/silindiğinde stats
etkisi işlem öncesinde açıklanır.

### Memories

1. Guest mesaj, emoji, opsiyonel display name ve uygun media ekleyebilir.
2. Email/telefon/ad zorunlu değildir.
3. Upload progress, processing, failure ve retry durumu görünürdür.
4. Creator-only gönderi public listede görünmez.
5. Public ayarda gönderi Ready olduktan sonra otomatik görünebilir.
6. Guest finalize sonrası edit/delete yapamaz; Creator hide/delete eder.
7. Rate limit veya dosya reddi private teknik detay sızdırmadan açıklanır.

### Gift Reservation

1. Guest requested ve remaining quantity görür; reserver identity görmez.
2. Uygun partial quantity seçer.
3. Guest name zorunlu; contact alanları decision register'a bağlıdır.
4. Başarı sonrası reservation kendi session'ına bağlanır.
5. Aynı browser management capability ile kendi reservation'ını iptal
   edebilir.
6. Concurrent conflict'te güncel remaining quantity gösterilir ve guest
   yeniden seçim yapar.

## 6. Super Admin Yolculuğu

1. Kontrollü oluşturulmuş hesapla login ve zorunlu MFA tamamlanır.
2. Aggregate dashboard user/account type, invitation state, plan,
   payment, storage ve system health gösterir.
3. Admin kullanıcı/account özetine ve ban işlemine gider.
4. Ban confirmation sebep/not ve login/public-invitation etkisini açıklar.
5. Plan/entitlement editörü value type, hard ceiling ve limit düşüşünün
   mevcut kullanıma etkisini gösterir.
6. Payment/subscription provider detayına boğulmadan operational state
   olarak gösterilir.
7. Önemli işlemler audit görünümünde izlenir.

Unban PRODUCT'ta henüz onaylı olmadığı için accepted ekran/eylem değildir.

## 7. Responsive Kabul Kriterleri

- Public, Creator ve Admin 320 CSS px genişliğe kadar yatay sayfa taşması
  olmadan kullanılabilir.
- 200% zoom bilgi veya eylem kaybettirmez.
- Mobil Creator wizard tek kolon ve mantıklı focus/alan sırasındadır.
- Temel devam/preview/publish eylemi erişilebilirdir; birden fazla ekranı
  kaplayan sticky alan kullanılmaz.
- Masaüstü editör iki kolon olabilir; dar ekranda preview ayrı panel veya
  fullscreen olur.
- Tablet masaüstünün sıkıştırılmış hali değildir.
- Media, map, uzun başlık ve form seçenekleri container dışına taşmaz.
- Admin tabloları mobilde kritik bilgiyi kaybetmeyen list/card veya
  kontrollü horizontal region kullanır.
- Touch target tercihen en az 44×44 CSS px'dir.
- Orientation değişiminde form verisi ve wizard adımı korunur.

## 8. Accessibility Kabul Kriterleri

Hedef **WCAG 2.2 AA**:

- Bütün akışlar yalnız klavyeyle tamamlanabilir.
- Görünür focus ve mantıklı focus order vardır.
- Route/modal/validation sonrası focus yönetilir.
- Her form control programatik label ve ilişkili error/help taşır.
- Publish error summary ilgili alana focus/link verir.
- Autosave/upload/async durumları ölçülü live region ile duyurulur.
- Renk tek durum göstergesi değildir.
- Normal metin 4.5:1, büyük metin 3:1 contrast hedefler.
- Semantic heading/landmark ve skip link bulunur.
- Sıralama yalnız drag'e bağlı değildir; keyboard action vardır.
- `prefers-reduced-motion` desteklenir.
- Video sesli autoplay yapmaz ve keyboard ile kontrol edilir.
- Loading/empty/error/success yalnız ikonla anlatılmaz.
- Dekoratif image screen-reader gürültüsü üretmez.
- Tarih, saat, para ve sayılar Türkçe locale ile anlaşılır gösterilir.

## 9. Faz 1 UX Deliverable'ları

1. Public/Creator/Admin route ve authorization matrisi.
2. Mobile-first Creator shell ve editor low-fidelity bilgi mimarisi.
3. Creation girişlerinin tek wizard use case'ine birleşmesi.
4. Autosave/Active update state modelinin microcopy'si.
5. Lifecycle action, confirmation, empty/inactive/error state matrisi.
6. Publish preflight ve immediate/scheduled publish wireframe'i.
7. RSVP/Memories/Gift guest ve Creator çift taraflı akış şemaları.
8. Responsive layout ve accessibility component contract'ları.
9. E2E/a11y acceptance scenario matrisi.

Faz 1 business UI geliştirmez; bu deliverable'lar yalnız shell,
contract ve foundation kararlarını besler.
