# Faz 1 — UX Foundation Sözleşmesi

Durum: **Accepted** (Reviewer pass 2026-09-29: 9/9 gerekli deliverable mevcut
ve yeterli derinlikte, kapsam ihlali veya PD-01–13 varsayımı bulunmadı,
docs/PRODUCT.md/UX_FLOWS.md/PHASE_0_BASELINE.md ile tutarlı; detay için
docs/PHASE_1_EXECUTION.md'nin M1U notuna bakın)  
Tarih: **2026-09-28**  
Kapsam: **Shell, akış, durum dili ve erişilebilirlik sözleşmesi; business UI değil**

Bu belge `docs/PRODUCT.md`, `docs/PHASE_0_BASELINE.md`,
`docs/UX_FLOWS.md`, accepted ADR'lar ve onaylı Faz 1 execution kaydını
uygulanabilir bir UX foundation sözleşmesine dönüştürür. Ürün kapsamını
genişletmez ve kesin görsel tasarım oluşturmaz.

Bu belgede kullanılan etiketler:

- **[Gereksinim]**: Product/baseline/accepted ADR kaynaklı, uygulama ve kabul
  testleri için bağlayıcı davranış.
- **[Foundation kararı]**: Ürün davranışını değiştirmeyen, Faz 1 shell ve
  erişilebilirlik temelinde uygulanacak teknik/etkileşim sözleşmesi.
- **[Öneri]**: Sonraki tasarım çalışmalarında doğrulanacak UX yönü; kabul
  edilmiş ürün davranışı değildir.
- **[Açık karar]**: Kullanıcı kararı olmadan seçeneklerden biri uygulanamaz.

## 1. Kapsam Koruması

### 1.1 Bağlayıcı ilkeler

- **[Gereksinim]** Ürünün ana hedefi, Creator'ın teknik veya tasarım bilgisi
  olmadan birkaç dakika içinde profesyonel bir davetiye oluşturabilmesidir.
- **[Gereksinim]** Ürün serbest biçimli veya drag-and-drop tasarım editörü
  değildir; şablon seçme ve yapılandırılmış bilgileri doldurma modelidir.
- **[Gereksinim]** Public, Creator ve Super Admin deneyimleri tek responsive
  React uygulamasında, telefon/tablet/masaüstünde çalışır.
- **[Gereksinim]** Authorization backend'de uygulanır. Route guard veya gizli
  navigasyon tek başına güvenlik sınırı değildir.
- **[Gereksinim]** Public code yalnız locator'dır; Creator veya Admin yetkisi
  vermez.
- **[Gereksinim]** Preview ile public davetiye aynı normalized render
  modelini ve renderer'ı kullanır; preview guest mutation veya view-stat
  üretmez.
- **[Gereksinim]** Faz 1 invitation CRUD/renderer, RSVP, Memories, Gift veya
  kapsamlı Creator/Admin business UI geliştirmez. Bu belgedeki feature
  akışları sonraki fazlar için kabul sözleşmesidir.

### 1.2 Bu belgede çözülmeyen kararlar

| Konu | Durum | Bu sözleşmenin koruduğu sınır |
| --- | --- | --- |
| Wizard'ın kesin adım sırası | **[Açık karar]** | IA sabit adım adı/sayısı varsaymaz; domain/API sıraya bağımlı olmaz. |
| Kullanıcıya gösterilecek varsayılan timezone | **[Açık karar]** | Tarih/saat özeti timezone'u görünür kılar; varsayılan seçilmez. Teknik instant UTC, timezone IANA ID'dir. |
| PD-07 Scheduled cancel/reschedule/hemen yayınlama | **[Açık karar]** | Scheduled ekranında bu eylemler eklenmez; karar sonrası action slot'una yerleşebilir. |
| PD-08 Trash restore hedef state'i | **[Açık karar]** | Restore eylemi hedef state sözü vermez; doğrulanmış sonuç backend'den gösterilir. |
| PD-09 Expired kaydı geleceğe schedule etme | **[Açık karar]** | Uygun yeni hakla accepted reactivation gösterilebilir; geleceğe schedule seçeneği eklenmez. |
| PD-10 Gift contact alanları ve retention | **[Açık karar]** | Yalnız zorunlu guest adı kesindir; contact control/alan/retention tasarlanmaz. |
| PD-13 Scheduled autosave publication semantiği | **[Açık karar]** | Scheduled için “başlangıçta otomatik yayınlanır” veya “Güncelle gerekir” denmez. |

## 2. Route, Shell ve Authorization Matrisi

Exact path'i kaynaklarda kesinleşmemiş rotalar bu tabloda route ailesi olarak
gösterilir. Path adı değişse de shell ve authorization sınırı değişmez.

| Zone / route | Erişim | Veri/yetki sınırı | Başarısız veya özel durum | Faz 1 shell yükümlülüğü |
| --- | --- | --- | --- | --- |
| Public `/sablonlar` | Anonymous dahil herkes | Yalnız active template katalog metadata'sı ve güvenli demo içeriği | Katalog hatası private detay sızdırmayan retry state'i verir | Public shell içinde lazy route; Creator/Admin navigasyonu yok |
| Public template demo preview, exact path TBD | Anonymous dahil herkes | Demo veri; gerçek Creator draft'ı veya private içerik yok | Bulunamayan/inactive template güvenli not-found state'i | Route contract için reserved; business renderer Faz 1 dışında |
| Public `/davetiye/:slug-:publicCode` | Anonymous dahil herkes; Creator/Admin de public kullanıcı gibi | Yalnız allowlisted public projection; slug dekoratif, `publicCode` locator-only | Effective Active ise full invitation; erken Scheduled/Paused/Expired/banned ise aynı PII-free inactive state; deleted/unknown ise 404 | Public invitation shell Creator/Admin nav ve public share butonu içermez |
| Kayıt/giriş/email verification/password reset route ailesi, exact paths TBD | Anonymous veya ilgili auth state | Identity use case'i; local allowlist dışı `returnUrl` kabul edilmez | Enumeration/private detail içermeyen feedback; HTTPS release gate backend'de | Public/auth shell; credential veya guest secret localStorage'a yazılmaz |
| Creator `/panel` | Authenticated Creator + Account | Yalnız current Account özeti | Auth yoksa protected content render edilmez; exact redirect/403 sunumu implementation contract'ında netleşir | Creator shell özeti; business dashboard Faz 1 dışında |
| Creator `/panel/davetiyeler` | Authenticated Creator + Account | Yalnız owner invitation listesi ve izinli state eylemleri | Empty/error state'leri ayrıdır; foreign ID hiçbir veri sızdırmaz | Creator shell route placeholder'ı |
| Creator `/panel/davetiyeler/yeni/*` | Authenticated Creator + Account; gerekli noktada email verification | Tek creation use case'i ve owner Draft | Paket/verification engeli “skip” olarak gösterilmez | Tek wizard shell; kesin adım sırası yok |
| Creator `/panel/davetiyeler/:id/*` | Authenticated Creator + query-level ownership | Owner edit/module/preview/publish-share/results alanları | Foreign/missing resource private veri sızdırmayan sonuç verir; API authoritative | Editor shell placeholder'ı; feature ekranı yok |
| Creator `/panel/cop-kutusu` | Authenticated Creator + Account | Yalnız owner'ın retention içindeki kayıtları | Empty state ve purge sonrası inaccessible state ayrıdır | Creator shell route placeholder'ı |
| Creator `/panel/plan-odeme` | Authenticated Creator + Account | Current Account plan/payment özeti | Commerce business akışları PD-12 ve sonraki fazlara bağlı | Shell placeholder'ı; checkout yok |
| Creator `/panel/hesap-guvenlik` | Authenticated Creator | Current identity/account güvenliği | Reauthentication/session davranışı backend policy'sine tabidir | Shell placeholder'ı |
| Admin `/admin` | Kontrollü bootstrap edilmiş, MFA-complete Super Admin | Aggregate platform özeti | MFA eksikse admin içeriği render edilmez | Creator'dan görsel/yetkisel olarak ayrı Admin shell |
| Admin kullanıcı/ban route ailesi | MFA-complete Super Admin | Aggregate hesap özeti ve accepted ban command | Özel RSVP/Memory/Gift içerikleri yok; unban PD-11 nedeniyle yok | Admin navigation placeholder'ı |
| Admin plan/entitlement route ailesi | MFA-complete Super Admin | Typed değerler, hard ceiling ve audit sınırı | Invalid değer inline + summary ile reddedilir | Admin placeholder; edit business UI Faz 1 dışında |
| Admin ödeme route ailesi | MFA-complete Super Admin | Operational/aggregate payment state | Raw provider payload/card data yok | Admin placeholder |
| Admin storage/health route ailesi | MFA-complete Super Admin | Aggregate operational state | Secret/provider credential yok | Admin placeholder |
| Admin audit route ailesi | MFA-complete Super Admin | Minimize edilmiş, access-controlled audit metadata | Özel guest içeriği veya secret yok | Admin placeholder |
| Global not-found | Herkes | Hiçbir protected veri yok | Shell'e uygun 404; public invitation deleted/unknown ile uyumlu | Her route zone için güvenli fallback |

Route guard sözleşmesi:

1. **[Gereksinim]** Guard değerlendirilirken protected shell içeriği veya önceki
   kullanıcının verisi bir an için bile gösterilmez.
2. **[Gereksinim]** Creator ownership client'tan gelen Account ID ile değil,
   authenticated principal ve query-level ownership ile belirlenir.
3. **[Gereksinim]** Admin, Creator kaynaklarına genel bypass kazanmaz; Admin
   shell özel guest içeriğini normal navigasyona eklemez.
4. **[Foundation kararı]** Route geçişinde shell hemen korunabilir; ana içerik
   erişim sonucu gelene kadar anlamlı bir loading state'i gösterir.
5. **[Foundation kararı]** Auth sonrası dönüş hedefi yalnız uygulama içi
   allowlisted route olabilir; dış URL veya protokol taşıyamaz.
6. **[Öneri]** Unauthorized ile not-found ayrımının kullanıcıya sunumu,
   kaynak keşfini kolaylaştırmayacak şekilde aynı sakin dil ailesini kullanır.

## 3. Mobile-first Creator Shell ve Editor Bilgi Mimarisi

### 3.1 Creator shell

```text
320 px ve üstü — tek kolon
┌────────────────────────────────┐
│ Menü  Sayfa başlığı    Hesap   │  landmark: banner
├────────────────────────────────┤
│ [Skip link hedefi / main]      │
│ Durum/plan bağlamı (varsa)     │
│                                │
│ Route ana içeriği              │
│ - birincil görev               │
│ - ilgili özet / liste          │
│ - loading/empty/error alanı    │
│                                │
├────────────────────────────────┤
│ Gerekli ise kompakt eylem alanı│
└────────────────────────────────┘

Geniş ekran
┌──────────────┬──────────────────────────────────────┐
│ Creator nav  │ Başlık / bağlam / hesap              │
│              ├──────────────────────────────────────┤
│              │ main route içeriği                   │
└──────────────┴──────────────────────────────────────┘
```

- **[Gereksinim]** Creator ve Admin shell aynı navigasyon veya yetki algısını
  paylaşmaz.
- **[Foundation kararı]** Mobil navigasyon kapalıyken focus alamaz; açıldığında
  modal/drawer focus sözleşmesine uyar.
- **[Foundation kararı]** Her route'un tek görünür `h1` başlığı ve benzersiz
  document title'ı olur.
- **[Öneri]** İlk bakışta tek bir baskın primary action gösterilir; hızlı
  invitation oluşturma hedefini zayıflatan eşit ağırlıklı eylem kümelerinden
  kaçınılır.

### 3.2 Creation/editor shell

```text
Mobil — current task odaklı
┌────────────────────────────────┐
│ Geri  Davetiye adı / Draft     │
│       Kaydedildi               │  autosave status
├────────────────────────────────┤
│ Süreç özeti / adım listesi     │  sıra ve toplam TBD
│ Current task başlığı           │
│ Açıklama + gerekli göstergesi  │
│                                │
│ Form / yapılandırılmış alanlar │
│ Alan yardım ve inline hata     │
│                                │
│ [Şimdilik Geç] yalnız optional │
├────────────────────────────────┤
│ Önizle          Birincil eylem │
└────────────────────────────────┘

Tablet
┌──────────────┬──────────────────────────────────────┐
│ Süreç/nav    │ Current task / form                  │
│              │ Preview ayrı panel veya fullscreen  │
└──────────────┴──────────────────────────────────────┘

Masaüstü
┌────────────┬──────────────────────┬─────────────────┐
│ Süreç/nav  │ Current task / form  │ Canlı preview   │
│            │                      │ veya placeholder│
└────────────┴──────────────────────┴─────────────────┘
```

- **[Gereksinim]** Mobil wizard tek kolon ve mantıklı DOM/focus sırasındadır.
- **[Gereksinim]** Telefon, tablet, masaüstü ve yeni sekmede tam preview
  seçenekleri erişilebilirdir. Viewport seçimi içeriği veya renderer'ı
  değiştirmez.
- **[Gereksinim]** Skip yalnız opsiyonel görevde görünür. Skip edilen modül
  kapalı kalır ve sonradan panelden açılabilir.
- **[Gereksinim]** Paket nedeniyle kullanılamayan feature “skip edildi” diye
  gösterilmez.
- **[Foundation kararı]** Progress gösterimi kesin adım sayısına veya sabit
  sıralamaya bağımlı olmaz. Adım isimleri feature contract'ıyla gelir.
- **[Foundation kararı]** Primary action alanı 320 px'de içerik ve odaklanan
  kontrolü örtmez; viewport'un birden fazla ekranını kaplayan sticky panel
  kullanılmaz.
- **[Öneri]** Desktop'ta preview üçüncü kolon olabilir; tablet “daraltılmış
  desktop” olmaz, preview drawer/fullscreen olarak açılabilir.

## 4. Tek Creation Giriş Sözleşmesi

Public katalog ve Creator Panel farklı başlangıç yüzeyleridir; ikisi de aynı
creation use case'ine bağlanır.

```text
Public template katalog ── template context (opsiyonel) ─┐
                                                         ├─> auth/verification gate
Creator Panel ────────── blank veya template context ────┘
                                                               │
                                                               v
                                                    tek owner Draft/use case
                                                               │
                                         yapılandırılmış görevler + autosave
                                                               │
                                             preview → preflight → publish
```

Sözleşme:

1. **[Gereksinim]** Her iki giriş de aynı backend creation davranışını ve aynı
   editor shell'ini kullanır; paralel “catalog wizard” ve “panel wizard”
   üretilmez.
2. **[Gereksinim]** Public katalogdan gelen template seçimi authorization,
   entitlement, active-template veya renderer validation'ını bypass etmez.
3. **[Gereksinim]** Auth/verification gerekiyorsa işlem tamamlandıktan sonra
   yalnız güvenli creation context'i korunabilir; credential/capability
   browser storage'a yazılmaz.
4. **[Gereksinim]** Optional görevler geçilebilir; required alanlar publish
   preflight'e kadar tamamlanabilir. Kesin wizard sırası bu sözleşmenin
   parçası değildir.
5. **[Foundation kararı]** Giriş kaynağı analitik veya yönlendirme bağlamı
   olabilir, fakat oluşan Draft'ın domain davranışını değiştirmez.
6. **[Foundation kararı]** Aynı creation isteğinin retry edilmesi kullanıcıya
   iki ayrı Draft oluşturmuş gibi gösterilmemelidir; kesin idempotency
   uygulaması ilgili backend milestone'unun sözleşmesidir.
7. **[Öneri]** Catalog başlangıcında seçilmiş template ve panel başlangıcında
   “şablonu sonra seç” bağlamı aynı ilk özet alanında görünür kılınabilir.

## 5. Autosave, Offline, Conflict ve Active Update Durum Modeli

### 5.1 Ortak durumlar ve microcopy

| Durum | Koşul | Görünür microcopy | Eylem | Duyuru |
| --- | --- | --- | --- | --- |
| Clean | Server ile eşleşen son revision | `Kaydedildi` | Yok | Her render'da duyurulmaz |
| Dirty/debounce | Anlamlı local değişiklik, istek henüz başlamadı | `Kaydedilmemiş değişiklikler var` | Otomatik kaydı bekle | Duyuru gerekmez |
| Saving | Autosave isteği sürüyor | `Kaydediliyor…` | Form kullanılabilir; duplicate save yok | Uzayan işlemde polite, tekrarsız |
| Saved | Yeni server revision alındı | `Kaydedildi` | Yok | Birleştirilmiş polite duyuru |
| Offline recovery | Network yok ve gerçekten kaydedilmemiş değişiklik var | `Bağlantı yok — değişiklikler bu cihazda geçici olarak korunuyor.` | `Bağlantıyı kontrol et` / otomatik retry | Bir kez polite |
| Save failed | Server'a yazılamadı, conflict değil | `Değişiklikler kaydedilemedi.` | `Tekrar dene` | `role=status`; tekrar eden hata spam'i yok |
| Revision conflict | Server revision beklenenden farklı | `Bu davetiye başka bir yerde güncellendi. Değişiklikleri karşılaştırıp çözmeniz gerekiyor.` | `Sunucudaki son sürümü göster`; local kurtarma kopyasını koru | Bir kez assertive/alert; focus dialog/summary'ye |
| Leaving with unsaved data | Yalnız gerçekten server'a ulaşmamış değişiklik var | `Kaydedilmemiş değişiklikleriniz var. Ayrılırsanız son değişiklikler kaybolabilir.` | `Sayfada kal` / `Ayrıl` | Modal adı ve açıklaması okunur |
| Active working saved | Active kaydın WorkingContent'i kaydedildi, PublishedContent değişmedi | `Değişiklikler kaydedildi; yayındaki davetiyede henüz görünmüyor.` | `Güncelle` | Save ve publish ayrı duyurulur |
| Active updating | Explicit update sürüyor | `Yayındaki davetiye güncelleniyor…` | Duplicate action disabled | Polite |
| Active updated | PublishedContent atomik yenilendi | `Yayındaki davetiye güncellendi.` | `Davetiye sayfasını aç` uygun olabilir | Polite |
| Active update failed | WorkingContent korunuyor, publish başarısız | `Değişiklikler kaydedildi ancak yayındaki davetiye güncellenemedi.` | `Tekrar güncelle` | Alert; kaydın kaybolmadığı açık |

### 5.2 Davranış kuralları

- **[Gereksinim]** Backend source of truth'tür. Browser storage yalnız bağlantı
  kurtarması içindir ve başarıyla server'a kaydedilmiş gibi gösterilemez.
- **[Gereksinim]** Revision conflict sessiz last-write-wins ile kapatılmaz.
  Kullanıcının local değişiklikleri çözüm gerçekleşene kadar korunur.
- **[Gereksinim]** Active invitation'da autosave yalnız WorkingContent'i
  günceller; public içerik yalnız explicit **Güncelle** ile değişir.
- **[Gereksinim]** Yalnız gerçekten kaydedilmemiş veri varsa navigation/browser
  leave uyarısı gösterilir.
- **[Foundation kararı]** Hızlı ardışık değişikliklerde live-region mesajları
  debounce/coalesce edilir; her tuş vuruşu duyurulmaz.
- **[Foundation kararı]** Conflict ekranı local revision'ı körlemesine server'a
  yazmaz. Kesin karşılaştırma/merge UI'ı invitation feature tasarımında
  belirlenir.
- **[Açık karar]** Scheduled autosave için PD-13 kapanmadan “başlangıçta
  otomatik yayınlanacak” veya “ayrıca Güncelle gerekli” microcopy'si
  kullanılmaz. Scheduled editor bu noktada nötr `Değişiklik kaydedildi`
  durumunu ve karar gerektiren ürün sözleşmesini taşır.

## 6. Lifecycle, Confirmation, Empty, Inactive ve Error Matrisi

### 6.1 Creator lifecycle görünümü

| Effective durum | Creator'ın kesin eylemleri | Confirmation / açıklama contract'ı | Eklenmeyecek açık davranış |
| --- | --- | --- | --- |
| Draft | Edit, preview, publish now veya schedule, delete | Delete, public erişimin zaten olmadığını ve retention boyunca trash'te kalacağını açıklar | Kesin restore sonucu sözü yok |
| Scheduled, başlangıç gelmedi | Preview ve planlanan zamanı gör; edit semantiği PD-13'e bağlı | Tarih/saat timezone ile okunur; public'in başlangıca kadar PII-free inactive olduğu belirtilir | Cancel, reschedule, hemen yayınla yok (PD-07); autosave publication sözü yok (PD-13) |
| Stored Scheduled, effective Active | Active action set'i ve template lock | “Yayın başladı” durumu yalnız worker state'ine bağlı olmadan gösterilir | Scheduled action set'i gösterilmez |
| Active | Edit WorkingContent, explicit Güncelle, preview, share, pause, sonuç yönetimi, delete | Pause: `Davetiyeniz geçici olarak yayından kalkar. Yayın süreniz durmaz ve bitiş tarihi değişmez.` | Active template doğrudan değişmez |
| Paused | Edit, template değiştir, preview, window uygunsa resume, delete | Template değişimi için pause gerekçesi görünür; resume öncesi current window/entitlement tekrar doğrulanır | Pause süresini uzatıyormuş gibi dil yok |
| Expired | İçerik/sonuçları gör, uygun yeni hak edin, accepted biçimde reactivate, delete | Reactivation aynı invitation kaydı, içerik ve public URL'yi korur | Gelecek tarihe schedule yok (PD-09) |
| Trash, retention içinde | Retention kalan süreyi gör, restore | Delete sonucu public erişimin hemen kapandığı; permanent purge sonrası geri alınamayacağı açıklanır | Restore sonrası Draft/önceki state sözü yok (PD-08) |
| Purged | Erişilemez | Kalıcı silme sonrası restore yok | Eski içeriğe veya provider media'ya erişim yok |
| Account banned overlay | Creator oturumu/işlemleri erişilemez | Invitation state'i değiştirmeden public delivery'nin kapandığı güvenli dil | Invitation state etiketi “Banned” yapılmaz |

Delete confirmation örnek sözleşmesi:

> **Davetiyeyi çöp kutusuna taşı?**  
> Public erişim hemen kapanır. Davetiye, sistemde tanımlı saklama süresi
> boyunca çöp kutusunda tutulur; ardından kalıcı olarak silinir.

Retention gün sayısı UI sabiti değildir; backend/system setting değerinden
gelir.

### 6.2 Empty ve unavailable durumları

| Bağlam | Başlık | Açıklama / action sınırı |
| --- | --- | --- |
| Creator invitation listesi boş | `Henüz davetiyeniz yok` | Hızlı creation girişine tek primary action; kapsam dışı örnek feature eklenmez |
| Trash boş | `Çöp kutunuz boş` | Retention veya restore eylemi gösterilmez |
| Sonuç modülü açık, kayıt yok | `Henüz yanıt yok` / feature'a uygun eşdeğer | “Modül kapalı” ile karıştırılmaz |
| Modül kapalı | `Bu bölüm davetiyede kapalı` | Creator'a daha sonra açabileceği anlatılabilir; Guest'e control gösterilmez |
| Feature entitlement nedeniyle yok | `Planınız bu özelliği içermiyor` | “Şimdilik Geçildi” olarak gösterilmez; plan action'ı sonraki commerce UX'e bağlıdır |
| Public inactive | `Bu davetiye şu anda yayında değil.` | `Yayın henüz başlamamış, yayın süresi sona ermiş veya davetiye geçici olarak pasife alınmış olabilir.` İsim, fotoğraf, tarih, OG/media veya neden ayrımı yok |
| Public deleted/unknown | `Sayfa bulunamadı` | Normal 404; invitation varlığı doğrulanmaz |

### 6.3 Error state ailesi

| Sınıf | Kullanıcıya sunum | Eylem/focus |
| --- | --- | --- |
| Field validation | Alan yanında somut Türkçe açıklama; yalnız renk/ikon değil | Submit sonrası error summary focus alır ve alan linkleri çalışır |
| 401/session yok | Protected içerik temizlenir; yeniden giriş gerektiği söylenir | Güvenli local return target ile auth girişine yönlenebilir |
| 403/not allowed | Yetki yok; resource owner veya policy detayı sızmaz | Güvenli geri dönüş |
| 404 | Kaynak bulunamadı; ID/code geçerliliği hakkında ek bilgi yok | İlgili shell ana sayfasına güvenli link |
| 409 revision/concurrency | Güncel server state'i ile çakışma olduğu açıklanır | Conflict çözüm yüzeyi focus alır; local kurtarma korunur |
| 422 business/preflight | Blocker ve warning ayrılır | Summary → ilgili alan/control focus linki |
| 429 rate limit | İşlemin şu anda çok sık denendiği söylenir; private teknik detay yok | Retry zamanı güvenilir ise anlaşılır biçimde gösterilir |
| 5xx/503 | `Şu anda işlemi tamamlayamıyoruz.` | Kaybı yanlış bildirmeyen retry; correlation ID yalnız destek için güvenliyse gösterilir |
| Offline | Geçici cihaz kopyası ve server'a kaydolmadığı açık | Reconnect/retry; success gibi gösterilmez |

## 7. Publish Preflight ve Publish Seçimi Low-fi

### 7.1 Preflight sınıfları

| Sınıf | Örnek kaynak | Davranış |
| --- | --- | --- |
| Required blocker | Template required alanı eksik; renderer uyumsuz; geçersiz tarih/window; entitlement veya hard limit uygun değil | Publish disabled; her blocker özet ve ilgili control linkiyle gösterilir |
| Recommended warning | Önerilen/opsiyonel alan eksik | Publish engellenmez; explicit `Yine de Yayınla` ile devam edilebilir |
| Informational | Public URL, noindex, seçilen başlangıç/bitiş ve timezone özeti | Karar değiştirmez; publish sonucunu anlaşılır kılar |

`required` ve `recommended` metadata template sözleşmesinden gelir; arayüz
bunları kendi içinde hardcode etmez.

### 7.2 Mobil wireframe

```text
┌────────────────────────────────┐
│ Yayınlama kontrolü             │  h1/dialog title
│                                │
│ Tamamlanması gerekenler (2)    │
│ ! Tarih eksik          [Git]   │  required blocker
│ ! Şablon alanı eksik   [Git]   │
│                                │
│ Öneriler (1)                    │
│ i Kapak görseli eklenmedi [Git]│  warning, non-blocking
│                                │
│ Yayın zamanı                    │
│ (•) Şimdi yayınla              │
│ ( ) Başlangıcı planla          │
│     [Tarih] [Saat] [Timezone]  │
│                                │
│ [ ] Bitiş zamanı ekle          │
│     plan hakkı sınırı özeti    │
│                                │
│ Sonuç özeti                    │
│ Başlangıç: … / Bitiş: …        │
│                                │
│ [Geri] [Yayınla]               │
│ warning varsa [Yine de Yayınla]│
└────────────────────────────────┘
```

Kurallar:

- **[Gereksinim]** Default primary publish davranışı immediate publication'dır;
  Creator planlama seçeneğini açarsa başlangıç tarih/saatini belirler.
- **[Gereksinim]** Opsiyonel bitiş, grant'in izin verdiği maximum publication
  window'u aşamaz.
- **[Gereksinim]** Required blocker varken `Yine de Yayınla` sunulmaz.
- **[Gereksinim]** Warning override bilinçli bir kullanıcı eylemidir; yalnız
  warning varsa sunulur.
- **[Foundation kararı]** Publish isteği sürerken duplicate submit engellenir,
  sonuç live region ile duyurulur ve başarısızlıkta kullanıcının form seçimi
  korunur.
- **[Foundation kararı]** Tarih/saat özeti timezone adıyla gösterilir; yalnız
  cihazın belirsiz yerel saatine güvenilmez.
- **[Açık karar]** Varsayılan timezone seçilmez. Scheduled olduktan sonraki
  cancel/reschedule/hemen yayınla seçenekleri wireframe'e eklenmez.

## 8. RSVP, Memories ve Gift Çift Taraflı Akış Sözleşmeleri

Bu bölüm business UI tasarlamaz; Guest ve Creator yüzeylerinin aynı use
case'lerde hangi veriyi görebileceğini ve hangi durumları açıklaması
gerektiğini gösterir.

### 8.1 RSVP

```text
Creator (owner)                              Guest (hesapsız)
──────────────────────────────────           ───────────────────────────────
Modülü aç                                    Effective Active public sayfa
  ↓                                            ↓
Başlangıç soruları oluşur                    Etkin soruları gör
  ↓                                            ↓
Düzenle / sil / sırala / required seç        Validate → gönder
  ↓                                            ↓
Public'e aç ───────────────────────────────> Başarılı submission
                                               ↓
Submission listesi + aggregate toplamlar    rsvp-manage capability cookie
  ↑                                            ↓
Güncellenmiş private cevap <─────────────── Aynı browser: Yanıtımı Güncelle
```

- **[Gereksinim]** Başka guest cevapları veya aggregate sonuçlar Guest'e
  gösterilmez; Super Admin normal yüzeyinde de bulunmaz.
- **[Gereksinim]** Capability yoksa önceki yanıt aranmaz; farklı cihaz/cookie
  kaybında duplicate mümkün olduğu gerçeği yanlış bir kesinlikle gizlenmez.
- **[Gereksinim]** Creator submission listesi ile aggregate toplamları ayırır.
  Semantic participant-count sorusu değiştirilmeden/silinmeden önce stats
  etkisi açıklanır.
- **[Foundation kararı]** Başarı ekranı “yanıtınız alındı” ile manage
  capability'nin aynı browser'a bağlı olduğunu farklı metinlerde açıklar.

### 8.2 Memories

```text
Creator (owner)                              Guest (hesapsız)
──────────────────────────────────           ───────────────────────────────
Modülü aç + görünürlük seç                   Effective Active public sayfa
  │  Creator-only / Public                     ↓
  └────────────────────────────────────────> Mesaj/emoji/display name/media
                                               ↓
                                             Upload progress / processing
                                               ↓
                                             Finalize → Ready veya failure
                                               │
Creator submission'ı görür <──────────────────┘
  ↓
Hide / delete
  ↓
Public visibility ise Ready içerik public listede otomatik görünebilir
```

- **[Gereksinim]** Display name opsiyoneldir; ad, email veya telefon zorunlu
  değildir.
- **[Gereksinim]** Creator-only gönderi public listede görünmez. Public
  görünürlükte Ready gönderi zorunlu approval queue olmadan görünebilir.
- **[Gereksinim]** Guest finalize sonrası edit/delete yapamaz; Creator her
  durumda hide/delete edebilir.
- **[Gereksinim]** Upload progress, processing, rejected/failure ve retry
  birbirinden ayrılır; teknik/provider detayı veya güvenlik kuralı sızdırılmaz.
- **[Foundation kararı]** Upload ve finalize ayrı async state olarak duyurulur;
  “yüklendi” mesajı Ready doğrulamasından önce kullanılmaz.

### 8.3 Gift Registry

```text
Creator (owner)                              Guest (hesapsız)
──────────────────────────────────           ───────────────────────────────
Item + requested quantity ekle               Requested / remaining gör
  ↓                                            ↓
Public'e aç ───────────────────────────────> Partial quantity + zorunlu ad
                                               ↓
                                             Reserve (transactional)
                                               ├─ success → GuestGiftSession
                                               └─ conflict → güncel remaining
Creator reserver adını ve qty görür            ↓
  ↓                                          Aynı browser: kendi rezervasyonunu
Reservation kaldırabilir                     capability ile cancel
```

- **[Gereksinim]** Diğer Guest'ler reserver identity görmez; yalnız seçilme
  veya miktar ilerlemesini görür.
- **[Gereksinim]** Reservation otomatik expire olmaz. Guest yalnız kendi
  session'ına bağlı reservation'ı iptal edebilir.
- **[Gereksinim]** Concurrent conflict'te güncel remaining quantity gösterilir
  ve Guest'ten yeniden seçim istenir; success izlenimi verilmez.
- **[Açık karar]** PD-10 kapanmadan contact input'u, alan etiketi, zorunluluk,
  saklama süresi veya Creator sunumu tasarlanmaz. Akışta yalnız genişletilebilir
  bir “optional contact — decision pending” contract noktası bulunur.

## 9. Responsive ve WCAG 2.2 AA Component Sözleşmesi

### 9.1 Reflow ve responsive davranış

- **[Gereksinim]** Public, Creator ve Admin shell 320 CSS px genişlikte yatay
  sayfa taşması olmadan kullanılabilir. Yalnız gerçekten iki boyutlu geniş
  içerik, kendi etiketli/keyboard erişilebilir scroll region'ında kalabilir.
- **[Gereksinim]** 200% browser zoom bilgi, kontrol veya işlemi kaybettirmez.
- **[Gereksinim]** Orientation değişimi form verisini ve current wizard
  context'ini kaybettirmez.
- **[Gereksinim]** Media, map, uzun başlık, uzun Türkçe metin ve form seçenekleri
  container'dan taşmaz.
- **[Gereksinim]** Touch target tercihen en az 44×44 CSS px'dir.
- **[Foundation kararı]** Breakpoint'ler cihaz adı yerine içeriğin sığma
  ihtiyacına göre seçilir. DOM source order mobildeki mantıklı okuma sırasıdır;
  CSS ile görsel yeniden sıralama focus sırasını bozmaz.
- **[Foundation kararı]** Admin veri tabloları dar ekranda kritik alanları
  kaybetmeyen card/list veya etiketli kontrollü scroll region'a dönüşür.

### 9.2 Ortak component contract'ları

| Primitive | Zorunlu erişilebilirlik/davranış contract'ı |
| --- | --- |
| `AppShell` | `header/nav/main` landmark'ları; main için skip-link target; shell'ler arası nav sızıntısı yok |
| `PageHeading` | Route başına tek görünür `h1`; document title route ile güncellenir |
| `Navigation` | Current item programatik belirli; mobile drawer kapalıyken erişilebilirlik ağacından/focus'tan çıkar |
| `FormField` | Programatik label; required bilgisi yalnız `*` değil; help/error aynı control ile ilişkilendirilir |
| `ErrorSummary` | Submit başarısızlığında focus alır; field error linkleri hedef control'e focus verir |
| `Button/Link` | Eylem ve navigasyon semantiği ayrılır; icon-only control accessible name taşır; disabled gerekçesi yakınında açıklanır |
| `Dialog/Drawer` | Açılışta anlamlı başlangıç focus'u; focus trap; Escape güvenliyse kapatır; kapanışta trigger'a focus döner |
| `AsyncStatus` | Persistent görsel durum + ölçülü live region; loading/success/error yalnız spinner/ikon/renk değildir |
| `Toast` | Tek bilgi kaynağı değildir; auto-dismiss kritik hata/kararı yok etmez; focus çalmaz |
| `Tabs/Segmented control` | Telefon/tablet/desktop preview seçimi gerçek label/state taşır; arrow-key davranışı seçilen pattern ile tutarlı |
| `PreviewFrame` | Accessible name; keyboard trap yok; preview/public renderer parity; simülasyon olduğu açıklanır |
| `DataList/Table` | Header-label ilişkisi; dar ekranda kritik bağlam korunur; scroll region keyboard ile erişilebilir ve adlandırılmıştır |
| `SortableList` | Drag zorunlu değildir; keyboard ile taşıma ve yeni sıra duyurusu vardır |
| `Media` | Anlamlı image alt metni; dekoratif image boş alt; video sesli autoplay yapmaz ve keyboard ile kontrol edilir |
| `StatusBadge` | Metin etiketi taşır; renk tek durum göstergesi değildir |

### 9.3 Focus sözleşmesi

1. Route değişiminden sonra focus, içerik hazır olduğunda route'un `h1` veya
   `main` başlangıcına programatik taşınır; shell nav tekrar okunmaya zorlanmaz.
2. Modal/drawer açıldığında focus başlık veya ilk anlamlı control'e gider;
   kapandığında tetikleyici hâlâ varsa ona döner.
3. Validation sonrası focus `ErrorSummary`'ye gider; kullanıcı linkle hatalı
   alana ulaşır. Her alan otomatik focus ile sırayla zıplatılmaz.
4. Async autosave/upload başarıları focus çalmaz. Kullanıcının bağlamı live
   region ile korunur.
5. Route guard sonucu auth/MFA gerektiğinde focus yeni sayfanın başlığına gider;
   protected içeriğe geri dönmez.
6. Destructive confirmation'da başlangıç focus'u, yanlışlıkla onayı tetikleme
   riskini azaltan güvenli control/başlık üzerindedir.

### 9.4 Live-region sözleşmesi

| Olay | Kanal | Kural |
| --- | --- | --- |
| Autosave saving/saved | `aria-live="polite"`, coalesced | Her keystroke yok; durum değişimi kısa ve tekil |
| Offline/online dönüş | `polite` | Yalnız değişimde; server'a kaydedilmemiş veri açıkça belirtilir |
| Revision conflict | `role="alert"` veya eşdeğer assertive | Bir kez; ardından focus çözüm yüzeyine |
| Form submit validation | Focus edilen summary + `role="alert"` uygunluğu | Hata sayısı ve linkli özet; inline hatalar ilişkili |
| Upload progress | Görsel progress + seyrek `polite` update | Yüzde değişiminin her adımı duyurulmaz; tamamlandı/başarısız kesin duyurulur |
| Publish/update sonucu | `polite` success, `alert` failure | Working saved ile public updated ayrımı korunur |
| Route loading | Gecikirse `status` | Anlık geçişlerde gereksiz “yükleniyor” gürültüsü yok |

### 9.5 Görsel ve içerik ölçütleri

- **[Gereksinim]** Normal metin en az 4.5:1, büyük metin en az 3:1 contrast
  hedefler; UI component/focus göstergeleri WCAG 2.2 AA kontrastını sağlar.
- **[Gereksinim]** Görünür focus vardır ve sticky/fixed içerik altında tamamen
  saklanmaz.
- **[Gereksinim]** `prefers-reduced-motion` desteklenir; işlev animasyona bağlı
  değildir.
- **[Gereksinim]** Türkçe tarih, saat, sayı ve TRY sunumu anlaşılır locale
  formatındadır; machine-readable değerler semantik kalır.
- **[Öneri]** Public invitation premium ve şablona özgü olabilir; Creator/Admin
  shell ise nötr, sakin ve göreve odaklı bir sistem dili kullanır. Üç shell'in
  ayrımı yalnız renkle yapılmaz.

## 10. E2E ve Accessibility Acceptance Matrisi

`F1` işaretli senaryolar Faz 1 feature'sız shell üzerinde otomasyona veya
kanıtlanabilir smoke testine uygundur. `Contract` işaretli senaryolar sonraki
business milestone'larında uygulanacak kabul sözleşmesidir; Faz 1'de sahte
feature UI üretme gerekçesi değildir.

| ID | Seviye | Alan | Senaryo | Beklenen sonuç |
| --- | --- | --- | --- | --- |
| UX-001 | F1 | Public shell | `/sablonlar` shell route'u 320 CSS px viewport'ta açılır | Yatay page overflow yok; skip link, landmark ve `h1` var |
| UX-002 | F1 | Creator shell | `/panel` 320 CSS px'de auth guard/loading/empty placeholder ile açılır | Protected içerik flash etmez; tek kolon, primary action görünür ve focus edilebilir |
| UX-003 | F1 | Admin shell | `/admin` 320 CSS px'de ayrı shell olarak açılır | Creator nav yok; MFA-required boundary private veri göstermeden sunulur |
| UX-004 | F1 | Shell boundaries | Public invitation route, Creator panel ve Admin shell navigasyonları karşılaştırılır | Public'te Creator/Admin nav ve share button yok; Admin/Creator nav birbirine sızmaz |
| UX-005 | F1 | Reflow | Public/Creator/Admin shell 200% zoom ile denenir | Bilgi/eylem kaybı ve iki boyut gerektirmeyen page-level horizontal scroll yok |
| UX-006 | F1 | Keyboard | Her shell yalnız Tab/Shift+Tab/Enter/Space/Escape ile gezilir | Focus görünür ve DOM sırası mantıklı; keyboard trap yok |
| UX-007 | F1 | Route focus | Shell içinde route değişimi yapılır | Document title güncellenir; focus yeni `h1/main` başlangıcına gider |
| UX-008 | F1 | Modal/drawer focus | Mobil nav veya foundation dialog açılıp kapanır | Focus içerde yönetilir; kapanışta trigger'a döner; arka plan focus alamaz |
| UX-009 | F1 | Async primitive | Loading → success ve loading → error örnek state'leri çalıştırılır | Persistent metin var; live-region tek ve ölçülü duyurur; success/error yalnız ikon değildir |
| UX-010 | F1 | Error boundary | Route-level beklenmeyen hata tetiklenir | Sanitized Türkçe fallback, retry/güvenli nav ve yönetilen focus; stack/private detail yok |
| UX-011 | F1 | Not-found | Her route zone'da bilinmeyen path açılır | Doğru shell veya güvenli global 404; protected veri yok |
| UX-012 | F1 | Contrast/motion | Automated a11y scan + reduced-motion emulation | Kritik axe ihlali yok; focus/contrast contract'ı karşılanır; motion işlev için zorunlu değil |
| UX-013 | Contract | Creation entry | Catalog template başlangıcı ve panel başlangıcı auth sonrası izlenir | İkisi aynı creation use case/editor shell'e gelir; template context yetkiyi bypass etmez |
| UX-014 | Contract | Wizard responsive | Wizard 320 px, tablet ve desktop'ta kullanılır; orientation değişir | Mobil tek kolon; adım/form state kaybolmaz; preview uygun panel/fullscreen davranışı gösterir |
| UX-015 | Contract | Skip | Optional ve required görevlerde skip görünürlüğü incelenir | Yalnız optional görevde; paket kilidi skip gibi görünmez |
| UX-016 | Contract | Autosave | Edit → saving → saved hızlı değişikliklerle çalıştırılır | Backend revision alınır; live-region spam yapmaz; gerçek unsaved data yoksa leave uyarısı çıkmaz |
| UX-017 | Contract | Offline recovery | Kaydedilmemiş değişiklik sırasında bağlantı kesilir ve geri gelir | Geçici cihaz kopyası açıkça belirtilir; success denmez; reconnect sonrası server doğrulaması yapılır |
| UX-018 | Contract | Conflict | İki session aynı revision'ı değiştirir | Sessiz overwrite yok; local kurtarma korunur; conflict yüzeyi focus alır |
| UX-019 | Contract | Active update | Active içerik edit edilir, autosave olur, sonra Güncelle yapılır | Autosave public'i değiştirmez; “henüz yayında değil” ve update sonucu ayrı duyurulur |
| UX-020 | Contract | Public inactive privacy | Erken Scheduled, Paused, Expired ve banned URL'leri açılır | Aynı PII-free unavailable shell; isim/foto/tarih/OG/private media/neden ayrımı yok |
| UX-021 | Contract | Public deleted | Deleted ve unknown code açılır | İkisi normal 404 davranışı gösterebilir; invitation varlığı sızmaz |
| UX-022 | Contract | Publish blockers | Required blocker ve recommended warning birlikte bulunur | Blocker alan linki/focus'u çalışır; override yok; blocker çözülünce warning için `Yine de Yayınla` var |
| UX-023 | Contract | Scheduled publish | Başlangıç/bitiş seçilir | Timezone görünür; bitiş grant sınırını aşamaz; PD-07/13 davranışı UI'a sızmaz |
| UX-024 | Contract | Preview safety | Preview'da RSVP/Memory/Gift ve media etkileşimleri denenir | UI simülasyonu dışında mutation/view-stat yok; public access oluşmaz |
| UX-025 | Contract | RSVP privacy | Bir Guest yanıt verir; ikinci Guest ve Admin sonuçları açmayı dener | Başka cevap/aggregate görünmez; owner Creator kendi sonuçlarını görür |
| UX-026 | Contract | Memory async | Media memory yüklemesi processing/reject/retry yollarından geçer | Progress ve doğrulanmış Ready ayrılır; finalize sonrası guest edit/delete yok |
| UX-027 | Contract | Gift concurrency | İki Guest aynı remaining quantity'yi eşzamanlı reserve eder | Overbook yok; kaybeden güncel remaining ile yeniden seçer; reserver identity public olmaz |
| UX-028 | Contract | Form accessibility | Validation error'lı form keyboard/screen reader ile gönderilir | Summary focus alır; linkler doğru control'e gider; label/help/error ilişkileri geçer |
| UX-029 | Contract | Sorting | RSVP question veya sıralanabilir içerik keyboard ile taşınır | Drag gerekmeksizin sıra değişir; yeni konum duyurulur |
| UX-030 | Contract | Lifecycle dialogs | Pause ve delete confirmation keyboard/screen reader ile açılır | Pause sürenin durmadığını; delete public erişim/retention etkisini açıklar; focus geri döner |

Faz 1 shell acceptance minimum set'i `UX-001`–`UX-012`'dir. Shell
implementation'ı henüz ilgili route'u güvenli placeholder olarak sunmuyorsa
senaryo, route açıldığında zorunlu hale gelen test kaydı olarak tutulur; test
geçsin diye business feature eklenmez.

## 11. M1U Completion Checklist ve Self-review

| M1U deliverable | Bu belgedeki karşılığı | Sonuç |
| --- | --- | --- |
| 1. Public/Creator/Admin route ve authorization matrisi | Bölüm 2 | Tam |
| 2. Mobile-first Creator shell/editor low-fi IA | Bölüm 3 | Tam |
| 3. Tek creation entry contract'ı; kesin sıra yok | Bölüm 4 | Tam |
| 4. Autosave/offline/conflict/Active update microcopy | Bölüm 5 | Tam |
| 5. Lifecycle confirmation/empty/inactive/error matrisi | Bölüm 6 | Tam; PD-07/08/09/13 açık |
| 6. Publish preflight + immediate/scheduled low-fi | Bölüm 7 | Tam; timezone ve scheduled sonrası davranış açık |
| 7. RSVP/Memories/Gift iki taraflı akışları | Bölüm 8 | Tam; business UI yok, PD-10 açık |
| 8. Responsive/WCAG component-focus-live contract | Bölüm 9 | Tam |
| 9. E2E/a11y acceptance matrisi | Bölüm 10 | Tam; F1 ve future Contract ayrıldı |

`docs/UX_FLOWS.md` ile self-review sonucu:

- Public, Creator ve Admin shell sınırları korunmuştur.
- Public invitation içinde paylaş butonu veya management navigasyonu
  eklenmemiştir.
- Creation hızlı, structured ve skip edilebilir tutulmuş; free-form editor
  davranışı eklenmemiştir.
- Preview/public renderer parity ve preview mutation yasağı korunmuştur.
- Working/Published ayrımı ile Active explicit **Güncelle** davranışı
  korunmuştur.
- Scheduled/effective Active, PII-free inactive, trash ve ban overlay
  ayrımları korunmuştur.
- RSVP private cevap, Memories visibility ve Gift capability/privacy sınırları
  korunmuştur.
- 320 CSS px, 200% zoom, keyboard, route/modal/validation focus ve async
  announcement ölçütleri testlenebilir hale getirilmiştir.
- PD-07, PD-08, PD-09, PD-10 ve PD-13 için ürün davranışı icat edilmemiştir;
  diğer Phase 0 açık kararları da bu foundation tarafından kapatılmamıştır.
- Faz 1 kapsamı dışında business ekranı, template tasarımı veya provider
  entegrasyonu tanımlanmamıştır.

Bu dokümanın kabulü M7A'nın feature'sız route shell ve accessibility
primitive'lerini uygulamasına izin verir; RSVP, Memories, Gift, Invitation ve
publish business UI implementasyonuna izin vermez.
