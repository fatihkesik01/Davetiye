# Faz 10 — Privacy, Retention & Integrated MVP Hardening Planı

Durum: **TAMAMLANDI — 7/7 milestone (Fatih kabulü, 2026-10-07); P10-M7 temiz GitHub CI tekrarı Phase 11'e ertelendi**
Bağımlılık: **Faz 3–9'un ilgili dilimlerinin uygulanması, tamamlanması ve bağımsız doğrulanması**

Fatih Phase 10'u 2026-10-06'da açıkça onayladı. Bu yedi milestone, aşağıdaki
ürün/hukuk karar kapılarına tabidir; faz onayı açık kararları çözmez ve
belirsiz davranış icat etme yetkisi vermez.

Kaynak: Bu taslak `docs/ROADMAP.md` §15 (Phase 10)'daki daha önce
hazırlanmış yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md`
biçimine dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap
ayrıntıyı kopyalamaz kuralına uygun olarak, bu iki belge zamanla
birbirinden sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Bütün MVP data graph'ını kapsayan invitation/account deletion, provider
purge/reconciliation, consent/retention, Creator aggregate insights ve
uçtan uca release candidate kalite kapısını tamamlamak. Bu faz var, çünkü
purge ve privacy, RSVP/Memory/Gift/Media/Payment veri sahipliği oluşmadan
tam kanıtlanamaz; final launch öncesi tüm dilimler birlikte çalışmalıdır:
Creator çöp kutusu/restore/purge ve kabul edilmiş account/data deletion
akışını kullanır; kendi aggregate istatistiklerini görür; ürün bütün olarak
tutarlı, erişilebilir ve performanslıdır.

## Kapsam

Ownership/deletion graph'ı, DB/provider purge, hesap silme, service-vs-
marketing consent, retention matrisi, Creator istatistikleri, entegre uçtan
uca doğrulama, performans/abuse/accessibility ve release-candidate
sertleştirme.

## Kapsam Dışı

Veri export'u (backlog); unique visitor tracking; PITR zorunluluğu; backlog
ürün özellikleri.

## Bağımlılıklar

Faz 3–9'un ilgili dilimlerinin tamamlanması.

## Bu Fazı Bloke Eden Açık Ürün Kararları

`[DECISION REQUIRED — FATIH]`:

- Hesap silme kapsamı/kullanıcı akışı: **Kabul edildi (Fatih, 2026-10-06):** kullanıcı talebi + e-posta doğrulaması; onaydan hemen sonra oturumlar iptal edilir, hesap ve tüm invitation/guest içeriği kalıcı purge kuyruğuna alınır; provider medya silme işleri retry edilir; hukuken tutulması gereken ödeme/audit kayıtları korunur.
- Kesin legal/ödeme/audit/log/backup retention süreleri: **Kabul edildi (Fatih, 2026-10-06):** kesin süreler Phase 11 hukuk incelemesine bırakılır; o zamana kadar otomatik silme yok. Audit için de önceki kabul korunur.
- Consent/privacy kapsamı: **Kabul edildi (Fatih, 2026-10-06):** zorunlu service notice; ayrı, varsayılan kapalı marketing opt-in; Phase 10'da isteğe bağlı cookie/tracking yok. Metinlerin hukuki yeterliliği Phase 11 incelemesine kalır.
- Accepted RPO/RTO: **Kabul edildi (Fatih, 2026-10-06):** RPO ≤24 saat / RTO ≤8 saat; gerçek restore ve kapasite doğrulaması Phase 11'de.
- Aktif Organization aboneliğinin hesap silme sırasındaki yenileme iptali: **Kabul edildi (Fatih, 2026-10-06):** silme e-postası doğrulandığı anda otomatik yenileme iptal edilir; otomatik iade yapılmaz ve ödeme kaydı retention kararına göre korunur.
- Hesap silme ile yarışan tek seferlik ödeme: **Kabul edildi (Fatih, 2026-10-06):** sağlayıcı daha sonra başarılı ödeme bildirse dahi kanıt saklanır, silinmiş hesaba grant/yayın açılmaz, otomatik iade yapılmaz; destek talebi manuel değerlendirilir.
- Mevcut hesaplarda zorunlu hizmet bildirimi: **Kabul edildi (Fatih, 2026-10-06):** mevcut Creator ilk girişte bir kez onaylar; onay gelene kadar Creator API erişimi kapalıdır. Super Admin kapsam dışıdır.
- M5 aggregate metrik semantiği: **Kabul edildi (Fatih, 2026-10-06):** RSVP yanıt adedi + aktif ParticipantCount sorusunun toplamı; yayınlanmış + gizlenmiş anılar; Ready Creator + Guest medyalarının toplamı; aktif GiftReservation kayıt adedi.
- Önceki fazlardan kalan downgrade/purge semantics (varsa): önceki faz planlarında tarandı; açık kalan karar bulunmadı.

Kesin retention süreleri Phase 11 hukuk incelemesi ve production öncesi
kararıdır; Phase 10 boyunca ilgili veriler için otomatik silme yapılmaz.
Bu yasal süreler M1'in tamamlanmasını veya geçici saklama politikasını
bekletmez. Kabul edilmiş silme/consent/DR kararları implementasyon
sözleşmesinin parçasıdır.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Veri/retention haritası | Faz 3–9 | Architect, Database, Security, Fatih/legal girdisi | Hesap, invitation, guest, media, audit/ödeme/log/backup ownership ve retention tam matrisi accepted; ödeme/audit legal retention içeriği purge'den ayrılır. Kesin süreler Phase 11'e kalır; PD-22 M3 kararıdır ve M1'i bloke etmez. |
| 2 | Invitation purge | 1 | Backend, Database, Security | Retention sonrası idempotent DB purge; geçerli upload capability expiry'si durable deletion kaydında korunur ve provider deletion en son capability sona erdikten sonra tamamlanır; tüm child data temizlenir; retry/crash güvenli. `MediaAsset` satırı silinse de provider-delete outbox/tombstone stable asset/provider kimliğini tutar; terminal hata kalıcı görünür, sayfalı biçimde uzlaştırılabilir ve ops-only retry komutuyla yeniden denenebilir. Gerçek provider acceptance Phase 11. |
| 3 | Hesap/veri silme | 1, 2 | Backend, Database, Security, UI/UX | **ACCEPTED 2026-10-06.** Email-verified delete immediately revokes sessions and app/API/public access, blocks new media URL/session issuance, and begins account/invitation/guest purge; previously issued signed media URLs may work until expiry/provider deletion. Identity ownership, minimized ban/audit history, idempotency, media retry and email/payment races are tested. Organization local auto-renewal stops at verification; a durable provider-cancellation intent remains pending/unconfirmed because no provider cancellation adapter exists; no automatic refund. Implementers: Backend + Database + Frontend. Reviewer/Tester: ACCEPT, no open findings. Security: ACCEPT, no open findings. Solution build 0 warnings/errors; UnitTests 490/490; focused PostgreSQL integration 26/26; account-deletion UI tests 5/5; web tests 265/265, build/lint/API check pass; EF model clean. |
| 4 | Consent/privacy UX | 1 | Frontend, Backend, UI/UX, Security, legal reviewer | **ACCEPTED 2026-10-06.** Service notice + marketing are separate/auditable; marketing default-off; privacy/terms draft surfaces await Phase 11 legal review; no optional tracking; legacy Creators gated until explicit notice acknowledgement. Implementers: Backend + Frontend. Reviewer: ACCEPT after fixing 1 Medium OAuth consent-forgery finding (0 open). Security: ACCEPT (0 open; missing-token regression added). Solution build 0 warnings/errors; consent/auth integration 10/10; full web tests 258/258; web build/lint/API check pass. |
| 5 | Creator aggregate insights | Faz 3/5/6/7 | Backend, Database, Frontend | **ACCEPTED 2026-10-06.** Existing owner-only stats endpoint returns page views, RSVP response count + active ParticipantCount total, Published+Hidden memories, Ready Creator+Guest media and active gift-reservation record count. No visitor IDs/cookies or guest PII. Reviewer and Security ACCEPT, no open findings. Solution build 0 warnings/errors; 114/114 focused PostgreSQL integration; 10K-row EXPLAIN uses existing RSVP invitation index; endpoint <2 seconds in test. Web 268/268, build/lint/API check pass; share/QR and optional-module Playwright 6/6. No migration. |
| 6 | Entegre UX/ürün tamamlanması | 2–5 | Frontend, UI/UX, Backend | Tüm Creator/Guest/Admin yolları, empty/error/lifecycle metni, SEO/public web; PRODUCT/UX flows tamamlanmış; 320 px, 200%, keyboard, WCAG otomatik/manuel kontroller geçer. **ACCEPTED 2026-10-07:** Fatih, gerçek browser-UI zoom ve manuel screen-reader/contrast oturumunu Phase 11 açık lansman önkoşulu olarak taşıma kararını verdi. Automated responsive/axe/keyboard evidence, Tester/Reviewer slice ACCEPT ve Security ACCEPT kaydedildi. |
| 7 | Release-candidate doğrulaması | 1–6 | Tester, Security, Reviewer, Architect | Temiz CI dışındaki yerel RC doğrulamaları tamam; CI/push/deployment tekrarı Fatih'in 2026-10-07 kararıyla Phase 11'e ertelendi. Critical/High yok; Medium'lar owner/disposition ile kayıtlı; backlog creep yok; RC artifact tanımlı. |

### P10-M1 — Kabul edilen veri ve retention sözleşmesi

Fatih'in 2026-10-06 kararları: hesap silme kullanıcı talebi ve e-posta
doğrulamasıyla başlar; doğrulama sonrası oturumlar anında iptal edilir ve
hesap ile invitation/guest içeriği kalıcı purge akışına hemen girer. Provider
medya silmesi retry/reconciliation ile tamamlanır. Service notice zorunlu,
marketing ayrı ve varsayılan kapalı opt-in'dir; Phase 10'da isteğe bağlı
cookie/tracking yoktur. RPO ≤24 saat ve RTO ≤8 saattir. Kesin yasal/operasyonel
retention süreleri Phase 11 hukuk incelemesine bırakılmıştır; o zamana kadar
ödeme, audit, log ve backup kayıtları için otomatik silme yoktur. Consent
copy'sinin hukuki yeterliliği ve gerçek restore/capacity kanıtı Phase 11
kapısıdır. Önceki fazlardaki downgrade/purge kararlarında açık ürün kararı
kalmamıştır.

| Veri sınıfı | Sahiplik / mevcut kaynak | Lifecycle ve silme sınırı | Retention / kanıt |
| --- | --- | --- | --- |
| Identity credentials ve giriş artefact'ları | `ApplicationUser`; Identity login/claim/token tabloları; kullanıcı | Silme e-postası onaylanınca login kapatılır, auth artefact'ları kaldırılır ve mevcut cookie/session geçersizleşir. Mevcut modelde DB session tablosu yoktur; aktif cookie revoke mekanizması ayrıca doğrulanmalı. | Doğrulanmış hesap silmede hemen; sağlayıcı dışında kopya tutulmaz. |
| Creator hesabı ve kişisel profil | `Account` + Identity profili; kullanıcı | Doğrulama sonrası profil/kimlik verisi silme akışına girer. Invitation/account listeleri, public erişim ve yeni işlem anında kapanır. | Onay sonrası hemen; hukuken saklanması gereken payment/audit verisine çıplak account ID dışında profil PII kopyalanmaz. |
| Ban ve Admin audit geçmişi | `BanRecord`, `AdminAuditRecord`; platform operasyonu | Ban/audit geçmişi invitation purge'dan bağımsız kalır. `BanRecord.AccountId` şu an `Account` silmesini RESTRICT eder; account/Identity satırları tombstone yapılır, audit/ban/payment history cascade edilmez. `BanRecord.Reason` ve `InternalNote` serbest metinleri hesap silmede redakte edilir; event zamanı ve sabit referanslar korunur. Tombstone tüm normal account/public/entitlement yüzeylerinden dışlanır. | Fatih'in kabulü: legal süre belirlenene kadar otomatik silme yok. Sabit pseudonymous ID'ler ve Ban metinlerinin redaksiyon kapsamı Phase 11 hukuk incelemesinde doğrulanır; audit actor/subject ID'leri PII projection'a dönüştürülmez. |
| Platform katalog ve ayarları | `TemplateDefinition`, `Plan`, `PlanEntitlement`, `SystemSetting`; platform | Hesap veya invitation silme çocuğu değildir; platform sahipliğinde kalır. `Plan`→`PlanEntitlement` ilişkisi cascade, `AccountPlanGrant`→`Plan` ilişkisi RESTRICT; var olan grant/satış snapshot'ları katalog değişikliklerinden bağımsızdır. | Kullanıcı bazlı retention uygulanmaz; plan ve ayar değişikliği Phase 9 kabul edilen yeni alım/mevcut hak semantiğini korur. |
| Invitation ve Creator içeriği | `Invitation` kökü, çalışma/yayın snapshot'ları, `PublicationWindow`; Creator | Normal Trash/restore'da 3 günlük varsayılan ayar korunur; retention sonrası mevcut permanent purge yürür. Hesap silmede Trash bekleme süresi uygulanmaz: erişim anında kapanır, purge hemen kuyruğa alınır. `PublicationWindow` invitation ile birlikte cascade silinir. | Invitation'a bağlı RSVP, memory, gift, media ve aggregate sayaç permanent purge kapsamındadır. |
| Guest verisi ve capability'ler | RSVP cevapları/management capability; Memory/name/media/upload capability; Gift reservation/name/contact/session; invitation | Invitation kalıcı purge'ında capability'ler ve PII'li child kayıtlar da silinir. Guest capability'leri hesabı silme talebinde ayrıca bir Creator hesabı gerektirmez; parent invitation purge onları kapsar. | Purge tamamlandığında DB içeriği kaldırılır; bu kayıtlar için bağımsız süre seçilmez. |
| Medya metadata ve dış provider bytes | `MediaAsset`, `MediaPlacement`, `PendingUpload`, `MediaProviderEvent`; Cloudflare Images/Stream nesneleri; invitation | DB metadata ile dış provider byte'ları ayrı sahipliktedir. Invitation purge idempotent provider-delete outbox işi üretir; iş başarıya/absent teyidine kadar retry/reconciliation sürer. Expired/rejected upload bytes PD-16 gereği invitation kalıcı purge edilene kadar tutulur. | Provider deletion doğrulama kanıtı ve hata/backlog metriği korunur; gerçek Cloudflare hesabı doğrulaması Phase 11'de. |
| Analytics aggregate | `InvitationViewTotal`; invitation | Yalnız toplam sayaç; visitor ID/cookie yok. Invitation permanent purge sayaç satırını cascade ile siler. | Creator erişimi yalnız owner-scoped aggregate; invitation retention ile aynı purge. |
| Grants, payment ve Organization billing | `AccountPlanGrant`, `PaymentAttempt`, `OrganizationSubscription`, billing cycles; account/payment modülleri | Hesap silme başladığında mevcut grant'lar revoke edilir ve minimum ilişki/settlement referansı olarak retained tutulur; silinmiş hesap hiçbir entitlement/public access üretemez. Invitation/account silme akışından finansal settlement kayıtları otomatik cascade edilmez. Yeni grant issuance engellenir. Önceden başlatılmış one-time payment silme başladıktan sonra başarılı olursa settlement kanıtı korunur, grant açılmaz ve refund otomatik yapılmaz; support talebi manuel inceler. Email verification anında local auto-renewal durdurulur ve provider cancellation intent dayanıklı olarak kuyruğa alınır; provider sonucu doğrulanmış sayılmaz ve dış sağlayıcı işlemi Phase 11 entegrasyon kapısıdır. | Kesin hukuki süre Phase 11'e bırakıldı; bu arada otomatik silme yok. Payment kayıtları grant ve invitation silinse de kanıt/audit için scalar ID ile yaşayabilir. |
| Consent kayıtları | Şu an persistence modeli yok; account | Phase 10'da service acknowledgement ve marketing opt-in ayrı, denetlenebilir kayıt olur; marketing varsayılan kapalı. | Consent tercih geçmişi yasal inceleme kararına kadar saklanır; kesin süre Phase 11. |
| E-posta ve entegrasyon inbox/outbox | Şifreli transactional-email envelope; generic inbox/outbox; platform operasyonu | Media-delete işi owner kaydından bağımsız tamamlanabilmeli. Silinmiş hesaba gidecek pending mail gönderimi bastırılmalı; generic work item'larında owner linkage/expiry kontrolü tasarlanmalı. | Email envelope'ın kendi bounded retry/age politikası geçerlidir; başka outbox/inbox retention'ı Phase 11 öncesi otomatik silinmez. |
| Uygulama/reverse-proxy/provider logları | Runtime operasyonu; uygulama ve altyapı | Loglarda secret/PII redaction; kullanıcı deletion DB purge'u geçmiş logları geriye dönük silmez. | Retention süresi Phase 11 hukuk/operasyon kararına kadar otomatik kısaltılmaz veya silinmez. |
| Şifreli off-site backup'lar | Database/operations; backup set | Restore yalnız yetkili operasyon prosedürüyle; silinen içerik backup expiry gelene kadar yedek kopyada kalabilir ve restore sonrası deletion replay/reconciliation gerekir. | RPO ≤24h / RTO ≤8h hedef; backup retention ve silinen verinin eski backup'lardan kaybolma süresi Phase 11 kararı. Otomatik backup silme yok. |

M1 teknik stratejisi: RESTRICT ilişkileri nedeniyle `Account` ve Identity
kullanıcı satırları fiziksel silinmek yerine aynı sabit GUID'leri koruyan,
giriş yapamayan ve PII'si temizlenmiş tombstone olarak tutulur; her
authenticated ve public erişim yolunda silinme durumu istek anında denetlenir.
Güvenlik damgası tek başına anlık session/cookie iptali sayılmaz. `BanRecord`
serbest metin alanları hesap silmede redakte edilir; pseudonymous tombstone
ID'lerinin hukuki kabulü Phase 11 incelemesinde doğrulanmalıdır. Invitation
child'larının çoğu cross-module scalar ID kullanır; cascade varsayımı
yapılamaz. Pending e-posta suppression, in-flight payment webhook/deletion
yarışının kilitlenmesi ve Organization renewal davranışı M3 teknik kabul
ölçütleri M3 kapsamına alındı. Security incelemesi mevcut purge yolunda iki
High bulgu belirledi; M2'ye devredildi:
aktif upload capability expiry'sinin asset temizliğinde kaybolması ve terminal
provider-delete hatasının asset satırı silinince reconciliation dışı kalması.
M1 kabul kaydı (2026-10-06): Architect ACCEPT, Database ACCEPT ve Security
ACCEPT; bu matris/veri sözleşmesi P10-M1'i kapatır. Tombstone kimliklerinin
hukuki kabulü ve kesin retention süreleri Phase 11'e bırakılmıştır; interim
otomatik silme yoktur. PD-22 hesap silme doğrulama anında yenileme iptali olarak cevaplanmıştır; M3 artık bu kararı beklemez. İki High purge bulgusu
M2'ye devredilmiştir.

### P10-M3 — Hesap silme uygulama ve kabul kaydı

2026-10-06'da tamamlandı. Kullanıcı talebi ve tek kullanımlık, hash'lenmiş e-posta
doğrulama token'ı ile hesap silme başlar. Doğrulamada oturumlar ve yeni Creator,
public ve medya erişim yetkileri kapatılır; davetiyeler mevcut M2 purge/outbox
yoluna alınır. Daha önce verilmiş imzalı medya URL'leri süreleri dolana veya
provider silmesi tamamlanana kadar çalışabilir. Hesap e-postalarına pending
outbox gönderimi deletion ile aynı hesap kilidi altında bastırılır; doğrulama ile
e-posta gönderimi ve ödeme settlement yarışları iki kilit sıralamasıyla test
edilmiştir. Deletion-first ödeme settlement'ı kanıtı saklar, grant/yayın veya
otomatik refund üretmez; payment-first grant'i deletion revoke eder. Identity
PII tombstone olarak anonimleştirilir, Ban serbest metni redakte edilir ve yasal
retention kayıtları Phase 11'e kadar otomatik silinmez.

Organization otomatik yenilemesi doğrulama anında uygulama içinde durdurulur ve
provider iptal niyeti kalıcı olarak kuyruğa alınır. Provider cancellation
adapter/worker ve gerçek sağlayıcı onayı mevcut değildir; sağlayıcı iptali
tamamlandı olarak gösterilmez ve Phase 11 entegrasyon kapısıdır. Otomatik iade
yoktur. Frontend bu durumu ve önceden üretilmiş medya linklerinin olası TTL
ömrünü açıkça bildirir.

Kabul: Reviewer/Tester ACCEPT ve Security ACCEPT; açık bulgu yok. Solution build
0 warning/error; backend UnitTests 490/490; M3 odaklı PostgreSQL integration
26/26; hesap silme UI tests 5/5; full web tests 265/265, build/lint/API contract
check geçti; EF model/migration tutarlı. Geniş bir public-access filtresinde
İlk geniş public-access koşusunda 4 test M4 Creator service-notice acknowledgement fixture'ı eksik olduğu için
428 Precondition Required aldı; M3 odaklı public erişim/deletion/security
kontrolleri geçti. Fixture'lar hizalandı ve sonraki geniş PostgreSQL filtresi 114/114 geçti; M3 için açık güvenlik bulgusu yok.

### P10-M2 — Invitation purge uygulama ve kabul kaydı

2026-10-06'da tamamlandı. Purge transaction'ı PendingUpload expiry, medya
türü ve provider asset kimliğini stable outbox kaydına yazar; intent/asset
satırları silindikten sonra da upload capability sona erene kadar normal
deletion ve terminal reconciliation bekler. Önceki v1 payload'lar okunur;
asset kalmış v1 Guest media work, aynı stable ID'yi koruyarak v2'ye atomik
yükseltilir; intent'i kalmamış legacy v1 iş en fazla eski 15 dakikalık
capability ömrü kadar bekler. Terminal kayıtlar sayfalı reconcile edilir;
provider absence ancak `DeleteNotBefore` geçtikten sonra tamamlanır.

Operasyon: `dotnet run --project tools/Davetiye.MediaDeletionRetry -- --list
--limit 100` terminal medya silme işlerini listeler; gerektiğinde sayfalama
için `--after <message-guid>` kullanılır. Belirli bir işi yeniden kuyruğa
almak için `dotnet run --project tools/Davetiye.MediaDeletionRetry --
--retry <message-guid> --confirm` çalıştırılır. Komut sabit medya deletion
tipiyle sınırlıdır, tek kayıt için açık onay ister ve diğer background
worker'ları başlatmaz. DB bağlantısı host configuration secret/env üzerinden
sağlanır; HTTP/Admin endpoint eklenmemiştir.

Kabul: Backend build 0 warning/error; focused PostgreSQL `MediaPersistenceTests`
ve permanent-purge regression 15/15; `EmailOutboxWorkerTests` 5/5; CLI help ve
geçersiz GUID argüman smoke kontrolleri geçti. Reviewer ve Security 2026-10-06
ACCEPT, açık bulgu yok. Gerçek Cloudflare upload-in-flight/provider silme
davranışı Phase 11 gate'idir. Migration yok; provider hesabına çağrı yapılmadı.

## Milestone Bağımlılık Grafiği

```text
M1 ──┬──> M2 ──┬──> M3 ──┐
     ├──> M4    │        ├──> M6 ──> M7
     └──> M5 ───┴────────┘
```

M2 (purge), M4 (consent) ve M5 (aggregate insights) M1'den sonra farklı veri
yüzeylerini etkilediği için paralel yürütülebilir. M3 (hesap silme) M2'ye
bağımlıdır (aynı purge mekanizmasını yeniden kullanır). M6 hepsinin üzerine
kurulur.

## Beklenen Uzman Rolleri

- **Architect:** veri/retention haritası, cross-phase tutarlılık review'u.
- **Backend + Database:** purge, hesap silme, aggregate insight sorguları.
- **Security:** provider purge doğrulaması, log redaction, consent
  minimization, entegre BOLA/capability matrisi.
- **Frontend + UI/UX:** consent UX, entegre ürün tamamlanması, SEO/public
  web, a11y.
- **Tester:** tam E2E, tüm kaynak BOLA, provider deletion retry/crash
  idempotency, retention time-travel, migration upgrade, load/abuse.
- **Reviewer:** faz sonunda kapsam/kalite bağımsız incelemesi.
- **Legal reviewer (Fatih/dış):** consent metinleri ve retention süreleri.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Tam E2E, tüm-kaynak BOLA ve provider deletion retry/crash-idempotency
  testleri geçer.
- Retention time-travel ve migration upgrade testleri geçer.
- Load/abuse, browser matrisi ve accessibility taramaları geçer.
- CI normalde bu kontrolleri ve container scan'lerini clean checkout'ta tekrarlar.
  Fatih 2026-10-07'de Phase 10 kapanışında yerel evidence'ı kabul ederek bu
  clean-checkout CI/push tekrarını Phase 11 P11-M8'e erteledi; P11-M8 go-live
  öncesi aynı release candidate üzerinde yeşil CI kanıtı üretmelidir.

## Faz Tamamlanma Kriterleri

- Release candidate işlevsel olarak tamamdır.
- Deletion/privacy/retention, accepted/legal-reviewed sözleşmeye göre
  implement edilmiştir.
- Açık blocker yoktur; Critical/High bulgu yoktur; Medium'lar owner/
  disposition ile kayıtlıdır.
- Backlog kapsam sızması yoktur (`docs/PRODUCT.md` §31 ile karşılaştırılmış).

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; RC commit/artifact, tam doğrulama
evidence'ı, kalan riskler, launch ön koşulları ve owner aksiyonları
günceller.

## Sonraki Faz Planlama Kapısı

Faz 10, Fatih'in 2026-10-06 tarihli açık onayıyla aktiftir. P10-M1–M5
tamamlanmıştır; M6 bütünleşik UX/ürün tamamlama milestone'u başlayabilir. PD-22 doğrulama
anında yerel otomatik yenileme iptali olarak uygulanmıştır; provider-side
cancellation halen doğrulanmamış Phase 11 entegrasyon kapısıdır. Faz 10'un tamamlanması, production provider
konfigürasyonunu ve kontrollü ticari lansmanı (Faz 11) mümkün kılar; Faz
11 ayrı planı ve Fatih'in explicit production/deployment yetkilendirmesini
gerektirir.

### P10-M5 — Creator aggregate insights implementation and acceptance record

Completed 2026-10-06. The existing owner-only `GET /api/v1/invitations/{invitationId}/statistics` endpoint returns six counts: page views; RSVP submission rows; the sum for the current active ParticipantCount Number question where each answer snapshot matches; Published+Hidden memories; Ready Creator+Guest media assets; and active gift reservation rows. Every scalar subquery is invitation-scoped. It does not load guest records, names/contact data, or visitor identifiers. The route retains verified-account and owner checks, rate limiting and no-store; privacy middleware metadata also applies no-store/noindex to authorization challenges and 404 responses. The UI presents all six metrics with accessible loading/error/retry states.

Acceptance: solution build 0 warnings/errors; aggregate/security PostgreSQL 2/2; full `PublicInvitationExperienceTests` 22/22; the PostgreSQL filter covering account deletion, payment/email races, Creator access and invitation statistics passed 114/114. On 10,000 representative RSVP rows, `EXPLAIN ANALYZE` used the existing `ix_rsvp_submissions_invitation_submitted_at` index and HTTP response completed under the test's 2-second ceiling. Web tests 268/268, build/lint/API contract check passed; optional modules and share/QR Playwright passed 6/6 across desktop, 320px and 200% zoom. Reviewer ACCEPT; Security ACCEPT after closing and re-reviewing the Low cache-header consistency note. No schema migration was needed.

### P10-M6 — Entegre UX / ürün tamamlama (uygulama ve doğrulama kaydı)

Implementation and automated verification are complete; Fatih accepted the
milestone on 2026-10-07 and moved the remaining manual verification limits to
Phase 11 launch prerequisites.
The public `/hesap-silme/onayla` route now resolves to the existing confirmation
page, unknown public fallthrough uses a not-found state, and UX flows now record
accepted consent, deletion, aggregate statistics, guest privacy and invitation
Open Graph behavior. E2E coverage fixes the Creator Memories gift-list fixture,
exercises Guest RSVP submit/reopen/update, and smoke-tests Admin overview,
accounts, plans, settings, templates, payments and audit routes. Security
review ACCEPT; no findings.

Verification: Vitest 269/269; Playwright 177/177 across desktop, 320 CSS px and
the `chromium-200pct` project; lint, typecheck, production build and API-client
check pass. Existing backend public-invitation integration tests cover active
Open Graph escaping, inactive privacy projection, deleted 404 and cache headers.
The current 200% project uses a 640 CSS px viewport plus CSS `zoom: 2`; it does
not change Chromium's browser UI zoom. Axe WCAG A/AA scans and keyboard-flow
tests pass on representative routes, but no dedicated screen-reader session or
independent manual contrast review was performed. These results are recorded
without claiming those manual checks or true browser UI zoom as passed. Fatih
accepted this disposition: real browser UI zoom plus manual screen-reader and
contrast review remain explicit Phase 11 launch prerequisites. M7 may proceed.

### P10-M7 — Integrated release-candidate verification and acceptance record

Independent Tester/Reviewer disposition on 2026-10-07 was **HOLD** solely
because clean-checkout CI had not run. On 2026-10-07, Fatih directed that
Git push and VPS deployment be left to Phase 11 and explicitly asked to finish
Phase 10 otherwise. This accepts the complete local M7 evidence and dispositions
the clean-checkout CI repetition as a Phase 11 M8 gate; it does not claim that
GitHub CI has passed. Security re-review is ACCEPTED with no open
Critical/High/Medium findings. The legacy
ownerless pending-email suppression gap was fixed by quarantining such rows
before payload decode or transport; the same-key concurrent-checkout 503
response now includes the attempt body, with its focused regression passing.

Latest local evidence: Release solution build 0 warnings/errors; UnitTests
491/491; ArchitectureTests 81/81; web Vitest 269/269; Playwright 177/177;
frontend lint/typecheck/build/API-client checks passed. Account-deletion and
email-dispatch focused integration tests passed 7/7. The full PostgreSQL
IntegrationTests run passed **527/527** after the narrow 503 response fix
(17m52s); its focused post-fix concurrency regression also passed 1/1. Trivy
found zero HIGH/CRITICAL items in the local API and web images and the source
scan; web and media-ingress npm audits report zero vulnerabilities. The local
RC candidate images are identified as `davetiye-api:m7-local`
(`sha256:75dc1a6e7e7a6d2a626826e493df927353bf98c223992b85ed5aec750f5a6fcc`)
and `davetiye-web:m7-local`
(`sha256:628268aedfd84fc9f484cbd551f7c239a54fb9cc03e60d238d6d3074464f9ed6`).

No standalone load harness was found; performance/abuse evidence includes the
M5 10,000-row EXPLAIN/under-2-second check and integration abuse/rate-limit
scenarios. Phase 11 clean-checkout CI, provider acceptance, and the M6 manual
accessibility/browser-UI-zoom checks remain explicit launch gates. Fatih
accepted P10-M7 on the local evidence with the CI repetition deferred as
recorded above. No commit, push, VPS, deployment, or live-provider operation
was performed.
