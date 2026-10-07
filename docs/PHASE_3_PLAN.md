# Faz 3 — Entitlement, Yayın Yaşam Döngüsü ve Public Davetiye Planı

Durum: **COMPLETE — Fatih 2026-10-01 tarihinde implementasyon onayı verdi; P3-M1–M8 tamamlandı (8/8, %100), 2026-10-02'de doğrulandı**
Bağımlılık: **Faz 2 tamamlandı ve bağımsız doğrulandı (8/8)**

Bu belge ilk kez Fatih'in açık talimatıyla 2026-09-29'da sıra dışı erken
planlama olarak hazırlandı. Faz 2, 2026-10-01'de bağımsız doğrulanarak
tamamlandı; Fatih aynı gün Faz 3 implementasyonunu açıkça onayladı ve P3-M1
karar paketini bütünüyle kabul etti. P3-M2 entitlement/grant temeli bağımsız
Architect, Security ve Tester+Reviewer kapılarından geçerek tamamlandı;
snapshot/window/publicCode temeli P3-M3 kapsamında 2026-10-02'de bağımsız
doğrulanarak tamamlanmıştır. P3-M4 lifecycle API ve Creator yayın yönetimi de
aynı gün bağımsız kalite kapılarından geçmiştir. P3-M5 public erişim kapısı ve
minimal public sayfa da 2026-10-02'de bağımsız doğrulanmıştır.

Kaynak: Bu taslak `docs/ROADMAP.md` §8 (Phase 3)'teki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Draft'ı güvenli biçimde yayınlanabilir hale getiren entitlement resolver,
Working/Published snapshot, lifecycle, publicCode, public projection,
trash/restore ve sharing/public-web contract'larını tamamlamak.

**Bu faz neden var:** Guest ve provider feature'ları ancak authoritative
public access gate ve publication window üstünde güvenle kurulabilir.

**Kullanıcıya görünen sonuç:** Creator publish/schedule/pause/resume/update/
expire/reactivate/delete/restore kullanabilir; Guest yalnız effective Active
public davetiyeyi görür; share/QR/OG/noindex çalışır.

## Kapsam

Grant assignment, quotas, PRODUCT §19–§21'deki DB-driven başlangıç
plan/entitlement/system-setting seed'leri, lifecycle commands/jobs, core
content modules (program, contact, announcement, FAQ, transport, countdown,
calendar, accepted map approach), public renderer, aggregate page views.

## Kapsam Dışı

RSVP, Memories, Gift mutations; Cloudflare media upload; iyzico; kapsamlı
Admin UI; production launch.

## Bağımlılıklar

Faz 2'nin uygulanması, tamamlanması ve bağımsız doğrulanması (normal
sıralama — bu taslağın erkenden yazılmış olması bu bağımlılığı ortadan
kaldırmaz); ADR-0002, ADR-0003, ADR-0004, ADR-0007.

## Kabul Edilmiş P3-M1 Ürün ve Lifecycle Sözleşmesi

Fatih aşağıdaki paketin tamamını 2026-10-01 tarihinde açıkça kabul etti:

1. **PD-01 — Free hak:** doğrulanmış hesap başına ömür boyu bir ücretsiz
   yayın hakkı vardır. Immediate publish ile hemen, Scheduled için pencere
   başlangıcında tüketilir. Başlangıç öncesi iptal hakkı tüketmez veya yeni
   hak üretmez; başladıktan sonra delete/expire yeni Free hak vermez.
2. **PD-03 — limit düşüşü:** kabul edilmiş Scheduled veya başlamış
   `PublicationWindow` geriye dönük kısaltılmaz. Yeni publish, reschedule ve
   reactivation güncel entitlement ile doğrulanır; açık revoke/ban ayrı bir
   erişim kapatma eylemidir.
3. **PD-05 — kota:** kota zaman aralığı çakışmasına göre değerlendirilir.
   Scheduled kendi `[startsAt, endsAt)` aralığı için atomik slot ayırır;
   Active ve Paused pencere bitene kadar slot tutar. Draft, effective Expired
   ve Trash sayılmaz; schedule cancel rezervasyonu bırakır.
4. **PD-07 — Scheduled eylemleri:** başlangıç öncesi cancel (`Draft`),
   reschedule ve publish-now desteklenir. Güncel entitlement/kota, preflight
   ve revision kontrol edilir; tarih/süre sessizce değiştirilmez.
5. **PD-08 — restore:** Trash restore her zaman `Draft` olur ve public erişimi
   otomatik açmaz. Aynı invitation/publicCode korunur. Kalan eski window varsa
   ayrı explicit publish işlemi quota'yı yeniden kontrol eder; bitmişse yeni
   grant gerekir.
6. **PD-09 — reactivation:** Expired invitation yeni uygun grant ile hemen
   veya ileri tarihte yeniden yayınlanabilir; yeni window açılır ve aynı
   invitation/publicCode korunur.
7. **PD-13 — snapshot:** schedule anında Published snapshot dondurulur.
   Autosave yalnız Working'i değiştirir; Scheduled/Active public içerik ancak
   explicit update ile atomik yenilenir. Başlangıç anında Working sessizce
   okunmaz.
8. **PD-04 premium-template alt vakası:** mevcut Published premium snapshot
   mevcut window sonuna kadar korunur. Güncel entitlement yokken yeni
   Published update/reactivation engellenir; free template seçmek veya
   entitlement'ı geri almak gerekir. Memories/Gift alt vakaları kendi
   fazlarına kadar açık kalır.
9. **Timezone:** görünür/değiştirilebilir varsayılan `Europe/Istanbul`;
   authoritative instant UTC, yeniden gösterim için IANA zone saklanır.
   Guest'e davetiyenin zone'u gösterilir; invalid/ambiguous DST zamanı
   sessizce çevrilmez.
10. **Harita:** first-party placeholder ve click-to-load Google Maps Embed;
    consent öncesi üçüncü taraf isteği yoktur. Google/Apple yönlendirme
    linkleri kalır; key yoksa güvenli fallback kullanılır; serbest `mapUrl`
    iframe kaynağı yapılmaz.
11. **OG:** yalnız effective Active için Published headline, sınırlı
    tarih/mekân özeti ve code-owned güvenli görsel kişiselleştirmesi yapılır.
    Tam adres/contact/internal ID/signed URL bulunmaz. Diğer durumlar generic
    PII-free metadata; deleted/unknown 404 döner.

Lifecycle invariantları: stored/effective state tek evaluator kullanır;
delete ve ban overlay'dir; worker gecikse de pencere saati authoritative'dir;
pause `endsAt` değerini uzatmaz; PublishedContent kendi template/version
pinine sahiptir; `publicCode` immutable locator-only değeridir; Creator
komutları owner scope + expected revision + transaction ile yürür.

PD-02 yalnız Organization subscription-backed grant yolunu bekletir; seed ve
diğer Phase 3 milestone'larını bloke etmez.

## P3-M4 Uygulama Sözleşmesi

2026-10-02'de M3 doğrulamasından sonra kabul edilmiş lifecycle davranışını
uygulamak için Architect, Backend ve Frontend'in belirlediği teknik sınırlar:

- Creator-only `GET /api/v1/invitations/{id}/publication` salt okunur durum,
  effective/stored state, sunucu saati, revision'lar, mevcut pencere ve
  Published pinleri, bekleyen değişiklikler, preflight ve kullanılabilir
  yayın hakkı seçeneklerini verir. GET yeni hak üretmez veya yayın başlatmaz.
- `POST /api/v1/invitations/{id}/publication/actions` eylemleri `publish`,
  `update`, `pause`, `resume`, `cancelSchedule`, `reschedule`, `publishNow`,
  `reactivate` olur. Authenticated owner, CSRF, HTTPS ve no-store sınırları
  korunur; publicCode yönetim yetkisi değildir.
- Komut `expected` içinde Invitation, Working ve varsa Published revision'ı
  ile current window ID/revision'ını taşır. Account lock ve owner-scoped row
  lock sonrası güncel veri/saat/hesap kontrolü yapılır; snapshot, window,
  grant ve state değişiklikleri birlikte commit veya rollback olur.
- Yayın formu yerel tarih/saat ve IANA zone gönderir. Backend geçersiz veya
  belirsiz DST saatlerini reddeder ve kabul edilen instant'ı UTC saklar.
  Publication zone seçimi, etkinlik içeriğinin zone'unu sessizce değiştirmez.
- `resume` içinde `publishWorkingContent=true` açıkça update + resume
  yapar; false mevcut Published'ı korur. Yeni snapshot güncel preflight ve
  premium hakkı ister; mevcut pencereye salt resume sayısal limit düşüşü
  nedeniyle yeni süre/kota uygulamaz. Ban/revoke kontrolleri devam eder.
- Başlangıç öncesi reschedule ve publish-now dondurulmuş Published'ı korur;
  Working'i yayımlamak ayrı explicit update'tir. Yeni aralık güncel
  entitlement ve kotayı geçer; formdaki tarihler sessizce değiştirilmez.
- Başlamış Scheduled pencere, worker gecikse veya bütün pencere kaçırılmış
  olsa bile eski hakkın tekrar kullanımına izin vermez. Reactivation yeni
  uygun hak ve yeni pencere açar. Cancel sonrası Draft'tan publish mevcut
  singleton Published'ı yeniler; eski pencerenin grant bağlantısı korunur.
- Creator arayüzü autosave ile public update'i ayırır; durdurmanın süreyi
  uzatmadığını, Active şablon kilidini ve recommended alanlar için açık
  onayı gösterir. Backend status ve preflight authoritative kalır.

M4; public access gate (M5), trash/restore/job (M6), paylaşım/QR/OG (M7)
ve sonraki fazların guest/provider/payment işlemlerini eklemez.

## P3-M5 Uygulama Sözleşmesi

- Anonymous `GET /api/v1/public/invitations/{publicCode}` immutable 64 hex
  locator'ı kullanır; dekoratif slug ve publicCode Creator yetkisi sağlamaz.
- Effective Active yanıtı yalnız Published snapshot'tan typed allowlist
  içerik ve kendi template/renderer/schema pinlerini verir. Working, internal
  ID, owner, grant ve private metadata public DTO'ya taşınmaz.
- Mevcut fakat erişilemeyen davetiye yalnız `status: unavailable` döndürür;
  malformed/unknown/deleted 404 olur. Inactive yanıtın sebebi, pencere bilgisi
  veya içerik pinleri açıklanmaz.
- Salt okunur tutarlı sorgu; güncel UTC, effective-state evaluator, Identity
  hesap kontrolü ve Plans-owned dar grant access portunu kullanır. Ban/revoke
  kapatır; kabul edilmiş pencereye güncel numeric/premium düşüşü uygulanmaz.
  Started Scheduled, grant henüz worker tarafından tüketilmemişken de kabul
  edilmiş rezervasyonla açılır; GET grant tüketmez.
- Nullable `DeletedAt` overlay'i, mevcut soft-delete convention'ının gerektirdiği
  nullable `PurgeAfter` alanı ve migration yalnız erişim temelidir; public,
  Creator read/command/reference ve kota sorguları silinmiş kayıtları dışlar.
  Delete/restore/purge işlemleri M6'da uygulanır.
- Public yanıtlar no-store/noindex ve rate limit uygular; ETag/304 yoktur.
  Frontend anonim public endpoint'i ve mevcut pinned renderer'ı kullanır;
  unavailable/error sonrası eski içerik tutulmaz. M5 metadata'sı generic'tir;
  paylaşım, kişisel OG ve kapsamlı public deneyim M7'ye kalır.

## P3-M6 Uygulama Sözleşmesi

**Kabul edilmiş ürün kararı (P3-M6-01):** Fatih, başlangıcı gelmemiş Scheduled
davetiye Trash'a taşındığında planlamanın iptal edilmesini ve kullanılmamış
hakkın serbest bırakılmasını açıkça seçti. Restore yine Draft olur; eski
planlama geri açılmaz, yeniden yayın ayrı açık onay ve güncel admission ister.

- Account/owner row lock ve revision kontrolü altında, taze UTC başlangıçtan
  önceyse kullanılmamış grant rezervasyonu aynı transaction içinde bırakılır,
  eski pencere historical yapılır ve Trash overlay'i uygulanır. Aynı Free
  grant satırı korunur; yeni Free hak üretilmez. Published/publicCode korunur.
- Release await'i sırasında başlangıç sınırı geçilirse işlem tamamen rollback
  olur; yeni istekte başlamış pencere davranışı uygulanır. Başlangıca eşit veya
  sonraki an iptal değildir: hak kabul edilmiş başlangıçta tüketilir, kalan
  pencere restore sonrası yalnız explicit republish ile kullanılabilir.
- İptal edilmiş pencere job tarafından etkinleştirilmez veya tüketilmez;
  purge, historical pencereyi temizlerken serbest grant'ı silmez. Restore
  sonrası normal publish güncel entitlement/preflight/kota ile yeni pencere
  açar; boş current window yerine eski planlama sessizce canlandırılmaz.

Silme isteği, revision tuple yanında onay ekranında gösterilen
`expectedRetentionDays` değerini taşır. Güncel ayarla uyuşmazlık
`409 RetentionChanged` döndürür; istemci durumu yenileyip yeni açık onay ister.
Geri yükleme, kabul edilmiş purge deadline'ını son durum sorgusundan sonra da
taze UTC ile kontrol eder; süre dolmuşsa transaction tamamen geri alınır.

- Owner-scoped trash listesi ve revision kontrollü trash/restore komutları;
  auth, HTTPS, CSRF, no-store ve rate limit sınırlarını korur. Restore daima
  Draft olur; `now >= purgeAfter` ise worker gecikse de restore reddedilir.
- Başlamış ve kalan accepted window restore sonrasında korunur; explicit
  `republish` aynı grant/window/publicCode/tarihleri koruyarak kalan aralık
  için güncel kotayı yeniden kontrol eder. Published korunması veya Working'in
  açıkça yayımlanması ayrı seçenektir; yeni snapshot güncel preflight/premium
  ister. Bitmiş pencere restore ile public erişim açmaz, yeni hak gerekir.
- `deletedInvitationRetentionDays` ayarı idempotent olarak 3 ile seed'lenir;
  DB değişiklikleri üzerine yazılmaz. Typed okuyucu 0–365 teknik güvenlik
  sınırında doğrular, eksik/geçersiz ayarda delete fail-closed olur. Delete
  sırasında kabul edilen purge tarihi sonraki ayar değişikliğiyle kısaltılmaz.
  Creator onayı güncel server policy/saatini gösterir.
- API host'unda configurable cadence/batch ile scoped BackgroundService;
  account/row lock sonrası taze UTC kullanır. Scheduled grant tüketimi kabul
  edilmiş başlangıç anını kullanır; state/expire/purge idempotent olur.
  Public erişimin güvenlik sınırı job değil M5'in request-time kapısıdır.
- Purge mevcut invitation-owned graph'ı DB cascade ile temizler; consumed
  grant'ın bare invitation ID bağlantısı hakkı yenilemeyen audit işareti olarak
  kalır. Henüz uygulanmamış media/payment/guest özellikleri eklenmez.

## P3-M7 Uygulama Sözleşmesi

- Content schema v1'e geriye uyumlu opsiyonel alanlar eklenir: `contacts`
  (ad, opsiyonel rol, telefon), tek güncel `announcement`, `faqs` (soru/yanıt)
  ve `transportStops` (ad, opsiyonel adres/saati). Yalnız typed allowlist; raw
  HTML, script, kişiye özel guest capability veya RSVP/memories/gift/media yok.
- ADR-0007 gereği mevcut renderer v1 kayıtları ve görünümü korunur. V2 yeni
  public-safe modülleri aynı normalize model üzerinden Creator preview ve
  public sayfada render eder; yeni template seçimi/catalog V2 pinler, eski
  Published snapshot'lar V1 ile render olmaya devam eder.
- Creator-only paylaşımda canonical publicCode URL kopyalanır, WhatsApp/native
  share ve QR göster/indirme sunulur. Public davetiyeye share butonu eklenmez;
  bağlantı hiçbir Creator yetkisi taşımaz.
- Countdown, Published `startsAt` ve IANA zone'u kullanıp sıfırda durur.
  Calendar çıktısı UTC DTSTART/escaped ICS'dir; ürün bitiş tarihi vermediği
  için DTEND/DURATION uydurulmaz. Harita first-party açıklama/placeholder
  gösterir; yalnız açık kullanıcı onayından sonra server-allowlist Google
  Embed yükler. Key yoksa Google/Apple yön tarifi bağlantıları kalır; serbest
  `mapUrl` iframe kaynağı olamaz.
- OG ilk HTML yanıtında, yalnız M5'in güncel Active allowlist gate'i geçerse
  sınırlı Published başlık/tarih/mekân özeti ve güvenli code-owned görseli
  içerir. API configured `PublicWeb:BaseUrl` ve güvenilir internal web shell
  origin kullanır, request Host'u kullanmaz; shell fetch sonrası gate/UTC
  tekrar okunur. Inactive generic/noindex, deleted/unknown 404, tüm yanıtlar
  no-store. Tam adres, kişi/telefon, internal ID, grant veya signed URL yok.
- Görüntülenme sayacı yalnız public UI'nin Active içerik başarıyla render
  etmesinden sonra aggregate total'ı bir artırır; Creator/preview, OG fetch ve
  polling sayılmaz. Visitor ID/cookie/unique analytics yok; Analytics sahipli
  sayacın invitation FK'si purge'da silinir.
- Scope RSVP, memories, gift, media upload, guest mutation ve public share CTA
  içermez.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| --- | --- | --- | --- | --- |
| P3-M1 | Karar ve lifecycle sözleşmesi | Faz 2 | Fatih, Architect, UI/UX, Security | **Tamamlandı (2026-10-01):** karar paketi ve state/action invariantları kabul edildi. |
| P3-M2 | Entitlement resolver, grant'lar ve ilk DB seed | M1 | Backend, Database, Security | **Tamamlandı (2026-10-01):** fail-closed typed resolver; doğrulanmış/banlı hesap ve owner contract'ı; account-level kota temeli; 4 plan/44 entitlement atomik seed; lifetime Free yarış güvenliği; migration ve bağımsız kalite kapıları geçti. |
| P3-M3 | Snapshot/window/publicCode | M1–M2 | Tamamlama: Backend (`p3_m3_backend`); doğrulama: Tester+Reviewer (`p3_m3_tests`), Security (`p3_m3_security`), Architect (`p3_m3_architect`) | **Tamamlandı (2026-10-02):** migration/backfill, bağımsız snapshot pinleri, immutable publicCode, grant/window bağlantısı ve atomik admission doğrulandı. İncelemeler ACCEPT; C/H/L: 0; 7 Medium giderildi (Backend 3, Security 3, Architect 1), açık bulgu yok. |
| P3-M4 | Lifecycle command'ları | M2–M3 | Backend (`p3_m3_backend`), Frontend (`p3_m4_frontend`, son DST hata eşlemesi: Orchestrator); doğrulama: Tester+Reviewer (`p3_m3_tests`), Security (`p3_m3_security`), Architect (`p3_m3_architect`), son yerel kapı: Orchestrator | **Tamamlandı (2026-10-02):** explicit publish/update, pause/resume, schedule cancel/reschedule/publish-now ve yeni grant ile reactivation; Creator UI, IANA/DST, revision/ABA ve yarışlar doğrulandı. Bağımsız incelemeler ACCEPT; C/H: 0; 2 Medium giderildi (reschedule onay metni, Organization grant admission); DST hata/test uyumu iyileştirildi, açık bulgu yok. Revoked cancel gözlemi güncel kod ve iki HTTP testiyle kapandı. |
| P3-M5 | Public access gate | M3–M4 | Backend (`p3_m3_backend`), Frontend (`p3_m4_frontend`); doğrulama: Tester+Reviewer (`p3_m3_tests`), Security (`p3_m3_security`), Architect (`p3_m3_architect`), son yerel kapı: Orchestrator | **Tamamlandı (2026-10-02):** anonymous Published allowlist, final UTC/ban/grant gate, PII-free inactive/404, read-only late-worker erişimi, deletion overlay ve Creator/kota filtreleri; no-store/noindex/rate limit ve minimal pinned public renderer doğrulandı. Bağımsız incelemeler ACCEPT; C/H/M/L: 0, açık bulgu yok. |
| P3-M6 | Trash, restore ve lifecycle job'ları | M4–M5 | Backend (`p3_m3_backend`), Frontend (`p3_m4_frontend`, retention onay düzeltmesi: Orchestrator); doğrulama: Tester+Reviewer (`p3_m3_tests`), Security (`p3_m6_security`), Architect (`p3_m6_architect`); son yerel kapı: Orchestrator | **Tamamlandı (2026-10-02):** configurable retention, owner/revision kontrollü trash/restore, aynı kalan pencereyle explicit republish ve idempotent expire/purge jobs. Başlamamış Scheduled Trash iptal edilir, unused grant bırakılır; restore Draft olur. Tester+Security+Architect ACCEPT; C/H/L: 0, 3 Medium giderildi (Tester 1, Security 2); odaklı gerçek PostgreSQL 50/50 ve Scheduled silme UX E2E 3/3 geçti. |
| P3-M7 | Public deneyim ve paylaşım | M4–M6 | Frontend (`p3_m4_frontend`), Backend (`p3_m3_backend`); doğrulama: Tester+Reviewer (`p3_m3_tests`), Security (`p3_m6_security`), Architect (`p3_m6_architect`) | **Tamamlandı (2026-10-02):** V1 renderer korundu, V2 typed bölümler, Creator paylaşım/QR, UTC takvim/geri sayım, onaylı Google haritası ve aggregate görüntülenme eklendi. PostgreSQL 20/20, tarayıcı 135/135; Reviewer+Architect+Security ACCEPT. Security 3 Medium giderildi (C/H/L: 0); açık bulgu yok. |
| P3-M8 | Faz kapanışı | M1–M7 | Tester+Reviewer (`p3_m3_tests`), Security (`p3_m6_security`); Architect (`p3_m6_architect`); son kapı: Orchestrator | **Tamamlandı (2026-10-03):** final Reviewer, Security ve Architect ACCEPT; Release build 0 uyarı, Unit 171/171, Architecture 66/66, PostgreSQL Integration 255/255, Vitest 104/104, Playwright 135/135 ve EF model/migration tutarlılığı geçti. İzole temiz kaynak kopyasında CI Release adımları, Compose API/web health (200), üç image HIGH/CRITICAL taraması ve kaynak/config taraması geçti. Bir High image CVE (CVE-2026-103111) düzeltildi: NGINX runtime Alpine 3.24'e alındı, `pcre2` 10.49-r0. C/H/M/L: 0 açık. GitHub runner çalıştırılmadı; değişiklikler commit/push edilmedi. |

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──> M3 ──> M4 ──┬──> M5 ──┐
                            ├──> M6 ──┤──> M8
                            └──> M7 ──┘
```

M2 ve M3, M1'in kabul edilmiş sözleşmesinden sonra sırayla ilerler (ikisi de
aynı `Working/PublishedContent` alanına dokunduğu için paralelleştirilmez).
M5, M6 ve M7; M4'ün lifecycle command'larına bağımlı olsa da birbirinden
farklı yüzeyleri (access gate / trash-job / public renderer) etkilediği için
dikkatli biçimde paralel yürütülebilir.

## Beklenen Uzman Rolleri

- **Architect:** ADR-0002/0003/0004/0007 sınırları, snapshot/window/publicCode
  tasarım review'u.
- **Backend + Database:** entitlement resolver, grant/quota transaction
  güvenliği, migration, lifecycle job'ları.
- **Frontend + UI/UX:** lifecycle UI, public renderer/content modülleri,
  paylaşım akışı, responsive/a11y.
- **Security:** public access gate, BOLA, publicCode guessing/rate-limit,
  inactive-state PII sızıntısı, trash/restore yetkilendirmesi.
- **Tester:** clock/timezone sınırları, transaction race'leri, iki-Creator
  BOLA, snapshot atomicity, job gecikmesi, a11y/E2E kanıtı.
- **Reviewer:** faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture testleri ve gerçek PostgreSQL Testcontainers integration
  testleri (özellikle concurrent quota ve iki-Creator foreign-read/mutation
  reddi) geçer.
- Saf unit test kapsamı zorunlu değildir (Fatih'in kararı: manuel/UI testi
  tercih edilir); izole edilmesi güç, kritik mantık için (örn. hard-ceiling/
  value-type/concurrency guard'ları) hedefli unit test eklenebilir ama şart
  değildir.
- Publish/update/schedule/pause/resume/expire/reactivate/delete/restore
  lifecycle command'ları için time-travel ve race-condition testleri geçer.
- Public access gate testleri: ban/delete/grant/state her request'te
  değerlendirilir; inactive/404 yanıtları PII sızdırmaz.
- publicCode guessing/rate-limit, snapshot atomicity ve trash/restore
  testleri geçer.
- Countdown/tarih-saat sınır testleri, mobile/a11y/OG testleri geçer.
- `npm run lint`, `npm run typecheck`, `npm test -- --run` ve production
  build geçer.
- CI clean checkout'ta bu kontrolleri ve container smoke'u tekrarlar.

## Faz Tamamlanma Kriterleri

- Public Active davetiye güvenli ve kullanılabilir; kapsam dışı
  RSVP/Memories/Gift/media/commerce işi eklenmemiştir.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer ve
  Architect onayı vardır.
- Açık Critical/High bulgu yoktur. Medium bulguların sahibi ve çözüm/defer
  gerekçesi `docs/AI_HANDOFF.md`'de yer alır.
- PD-01, PD-03, PD-05, PD-07, PD-08, PD-09, PD-13 ve Phase 3'e dokunan
  PD-04 premium-template alt vakası Fatih tarafından açıkça cevaplanmış ve
  yukarıdaki sözleşmeye kaydedilmiştir.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md` ve `docs/ROADMAP.md`; gerçek commit/branch
durumu, her milestone durumu, çalıştırılan komutların sonuçları, açık
bulgular/teknik borç, PD bağımlılıkları ve sonraki önerilen işi
günceller — aynı ayrıntılı test/finding metni birden fazla dosyaya
kopyalanmaz (`docs/AI_WORKFLOW.md` §14).

## Sonraki Faz Planlama Kapısı

Fatih Faz 3 implementasyonunu ve P3-M1 karar paketinin tamamını
**2026-10-01'de açıkça onayladı**. P3-M1–M8 tamamlandı ve bağımsız doğrulandı.
Faz 4 planı mevcut olsa da ayrı açık kullanıcı onayı olmadan implementasyon
başlatılmaz.
