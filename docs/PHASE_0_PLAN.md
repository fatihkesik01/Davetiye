# Faz 0 — MVP Teknik Baseline

Durum: **Tamamlandı; açık ürün kararları aşağıdaki decision register'da**
Tarih: **2026-09-28**
Bağımlılık: **Yok — foundational faz; yalnız `docs/PRODUCT.md`'nin ürün kapsamı kaynak olarak kabulüne dayanır**

Bu belge, diğer tüm `PHASE_N_PLAN.md` dosyalarıyla aynı format ve bölüm
sırasını kullanır (bkz. `docs/PHASE_TEMPLATE.md`). Faz 0 bir implementasyon
fazı değil, implementation öncesi teknik sınırları kesinleştiren bir
baseline/decision-register fazıdır; bu yüzden "Milestone" burada kod değil,
kabul edilmiş birer teknik karar bölümünü ifade eder. Ayrıntılı içerik,
kaynak kod yorumlarındaki (`docs/PHASE_0_PLAN.md §N`) referansların geçerli
kalması için orijinal numaralandırmasıyla belge sonunda Ek olarak durur.

`docs/PRODUCT.md` ürün kapsamı için en üst kaynaktır; bu belge yeni ürün
özelliği tanımlamaz, yalnız kabul edilmiş ürün davranışlarının teknik
sınırlarını belirler. İlgili kayıtlar: `docs/THREAT_MODEL.md`,
`docs/UX_FLOWS.md`, `docs/PHASE_1_PLAN.md`, `docs/adr/`.

## Amaç

Invitation, RSVP, Memories, Gift, payment ve Creator/Admin business
feature'ları implement edilmeden önce; MVP kapsam sınırını, modüler
monolith mimarisini, principal/authorization modelini, public/anonim trust
boundary'lerini, lifecycle/entitlement/media/payment trust sınırlarını ve
açık ürün kararlarını (PD-01–PD-13) tek kaynakta kesinleştirmek.

## Kapsam

MVP'ye dahil ürün alanlarının tam listesi ve kesin sınırları Ek §1'deki
kapsam matrisindedir (hesap, auth, template katalog, davetiye oluşturma/
preview, lifecycle, public davetiye, içerik modülleri, harita, media, RSVP,
memories, gift registry, paylaşım, plan/entitlement, ödeme, e-posta, Creator
Panel, Super Admin, analytics, privacy/deletion, dil/para). Modüler
monolith uygulama/domain sınırları Ek §2'de, yüksek seviyeli entity
ilişkileri Ek §3'tedir.

## Kapsam Dışı

`docs/PRODUCT.md` §31'deki backlog'un tamamı: organization membership/
workspace, guest hesabı, kişiye özel link, guest import, kişi bazlı
tracking, native mobil, SMS/otomatik WhatsApp/push, drag-and-drop editör,
kullanıcı template kodu, version history, invitation copy.

## Bağımlılıklar

`docs/PRODUCT.md`'nin ürün kapsamı olarak kabulü. Başka bir faz veya ADR
bağımlılığı yoktur — Faz 0, sonraki tüm fazların teknik baseline'ıdır.

## Bu Fazı Bloke Eden Açık Ürün Kararları

Faz 0'ın kendisi PD-01–PD-13'ten hiçbirini kapatmadı; bu fazın ilk çıktısı,
kararların **unresolved** olarak kayıt altına alınmasıydı (Ek §12). Sonraki
fazlarda alınan kararların güncel durumu aynı decision register'a işlenir;
ilgili business feature başlamadan açık kalan kararlar kullanıcı tarafından
cevaplanır.

## Milestone'lar

2026-09-29'da, `docs/AI_WORKFLOW.md` §4'ün "ilgili işleri birleştir, aşırı
bölme" kuralına uygun olarak orijinal 11 satırlık tablo 7 gruba konsolide
edildi (içerik/karar kaybolmadı — her grup hangi Ek bölümlerini kapsadığını
gösterir).

| No | Milestone (karar bölümü) | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü | Durum |
| ---: | --- | --- | --- | --- | --- |
| 1 | Mimari ve veri sınırları modeli (Ek §2–3) | Yok | Architect, Database | Modüler monolith yapısı, modül sahiplik tablosu, dependency yönü (ADR-0001) ve entity ilişki modeli tanımlı | Tamamlandı |
| 2 | Principal ve authorization matrisi (Ek §4) | 1 | Architect, Security | Creator/Guest/Super Admin işlem matrisi ve anonymous mutation policy tanımlı | Tamamlandı |
| 3 | Public ve anonim trust boundary — `publicCode`, RSVP/Gift/Memories capability (Ek §5–6) | 2 | Security | CSPRNG entropy, allowlisted projection, PII-free inactive state ve capability/HMAC kuralları tanımlı | Tamamlandı |
| 4 | Authentication, Google OAuth, Admin MFA (Ek §7) | 2 | Security, Backend | Cookie/session, Google flow ve admin bootstrap/MFA sınırları tanımlı | Tamamlandı |
| 5 | Lifecycle state machine ve Plan/Grant/Entitlement modeli (Ek §8–9) | 2 | Architect, Backend | Stored/effective state ayrımı, geçiş kuralları ve effective entitlement resolver sözleşmesi tanımlı | Tamamlandı |
| 6 | Media ve payment webhook trust boundary (Ek §10–11) | 3, 5 | Security, Backend | Presigned upload/quarantine, signed delivery, webhook idempotency ve reconciliation kuralları tanımlı | Tamamlandı |
| 7 | Açık ürün kararları decision register — PD-01–PD-13 (Ek §12) | 1–6 | Architect | Faz 0 kapanışında açık kararların hepsi unresolved olarak kayıt altındaydı; sonraki kabul durumu satır bazında güncellenir | Tamamlandı |

## Milestone Bağımlılık Grafiği

M1 → M2 → (M3, M4, M5 paralel) → M6 (M3 ve M5'e bağımlı) → M7 (hepsine
bağımlı, kapanış). Tüm milestone'lar tek oturumda, aynı belge içinde
üretildiği için gerçek paralel çalışma uygulanmadı; bağımlılık sırası
yalnız mantıksal okunabilirlik içindir.

## Beklenen Uzman Rolleri

Architect (mimari/sınır kararları), Security (trust boundary/threat model),
Backend ve Database (teknik fizibilite girdisi), UI/UX (Faz 1'in UX
foundation'ına girdi).

## Doğrulama Kriterleri

Bu faz kod üretmediği için doğrulama, implementasyon testi değil belge
tutarlılığı kontrolüdür: MVP kapsam matrisi `docs/PRODUCT.md` ile çelişmez;
modül sınırları ADR-0001 ile tutarlıdır; Faz 0 kapanışı sırasında
PD-01–PD-13'ün hiçbiri accepted olarak işaretlenmemiştir;
`docs/THREAT_MODEL.md` ve `docs/UX_FLOWS.md` bu belgeyle çelişmez.

## Faz Tamamlanma Kriterleri

- MVP kapsamı ve backlog sınırı kayıt altındadır (Ek §1).
- Domain, principal, public ve external provider trust boundary'leri
  kesinleşmiştir (Ek §4–§11).
- Faz 0 kapanışındaki açık ürün kararları accepted gibi gösterilmemiştir;
  sonraki fazlarda alınan kararlar Ek §12'de tarihli olarak güncellenir.
- Faz 1'in yalnız foundation kapsamıyla başlayabileceği, business feature
  implementasyonunun Faz 1 kapsamında olmadığı açıktır.

## Handoff Gereksinimleri

`docs/AI_HANDOFF.md`, Faz 0'ın kabul edildiğini ve o tarihte PD-01–PD-13'ün
unresolved kaldığını kaydetmiştir; sonraki fazlar yalnız decision register'da
açıkça kabul edilen kararları kapatılmış sayar.

## Sonraki Faz Planlama Kapısı

Bu fazın tamamlanması, Faz 1'in otomatik başlamasına izin vermez. Faz 1
yalnız bu belgenin ve ilgili ADR'ların kabulünden sonra, ve yalnız
foundation kapsamıyla (`docs/PHASE_1_PLAN.md`) başlayabilir.

---

## Ek: Detaylı Teknik İçerik (orijinal numaralandırma korunmuştur)

Aşağıdaki bölümler, kaynak kod yorumlarındaki (`docs/PHASE_0_PLAN.md §N`
biçimindeki) düzinelerce referansın geçerli kalması için orijinal `§`
numaralandırmasıyla korunmuştur; yukarıdaki özet bölümler bu içeriğin
tekrarı değil, ona giriş/işaretçidir.

### 1. MVP Kapsam Matrisi

| Alan | MVP | Kesin sınır |
| --- | --- | --- |
| Hesap | Dahil | Individual ve tek sahipli Organization |
| Authentication | Dahil | Email/password, doğrulama, reset ve Google |
| Super Admin auth | Dahil | Kontrollü bootstrap, zorunlu MFA |
| Template katalog | Dahil | Public katalog, demo preview, yaklaşık 8 kod tabanlı template |
| Davetiye oluşturma | Dahil | Wizard, backend autosave, skip edilebilir opsiyonel adımlar |
| Preview | Dahil | Telefon/tablet/desktop/yeni sekme; public ile ortak renderer |
| Lifecycle | Dahil | Draft, Scheduled, Active, Paused, Expired ve ayrı trash overlay |
| Public davetiye | Dahil | Tek URL, publicCode lookup, varsayılan noindex |
| İçerik modülleri | Dahil | PRODUCT.md bölüm 8'deki modüller |
| Harita | Dahil | Opsiyonel interaktif harita ve Google/Apple yönlendirmesi |
| Creator media | Dahil | R2 + Images Transformations, Stream, direct upload |
| RSVP | Dahil | Düzenlenebilir sorular, anonim create/update, private cevaplar |
| Memories | Dahil | Mesaj/emoji/foto/video, Creator-only/Public, Creator hide/delete |
| Gift Registry | Dahil | Partial quantity, guest cancel, Creator kaldırma |
| Paylaşım | Dahil | Creator Panel link/WhatsApp/native/QR; public sayfada buton yok |
| Paket/entitlement | Dahil | DB-driven Free/Standard/Premium/Organization |
| Ödeme | Dahil | iyzico production adapter'ı, development fake adapter |
| E-posta | Dahil | Resend production adapter'ı, development local/fake sender |
| Creator Panel | Dahil | Kendi davetiyeleri, sonuçlar, medya, plan/ödeme ve trash |
| Super Admin | Dahil | Aggregate yönetim, ban, plan, ödeme, storage, health ve audit |
| Analytics | Dahil | Aggregate toplamlar; visitor ID/unique visitor yok |
| Privacy/deletion | Dahil | Minimizasyon, consent ayrımı, retention ve kalıcı purge |
| Dil/para | Dahil | Türkçe ve TRY; geleceğe uyum feature değildir |
| Backlog | Hariç | PRODUCT.md bölüm 31'in tamamı |

Özellikle hariç tutulanlar: organization membership/workspace, guest
hesabı, kişiye özel link, guest import, kişi bazlı tracking, native
mobil, SMS/otomatik WhatsApp/push, drag-and-drop editör, kullanıcı
template kodu, version history ve invitation copy.

### 2. Uygulama ve Domain Sınırları

Tek React uygulaması, tek ASP.NET Core modular monolith, tek PostgreSQL,
tek `DbContext` ve tek migration hattı kullanılır. Background işler aynı
deployable içindeki worker'lardır. Mikroservis, broker ve Kubernetes MVP
bağımlılığı değildir.

| Modül | Sahip olduğu alan | Dar dış sözleşme |
| --- | --- | --- |
| Identity & Accounts | User, Account, external login, MFA, ban | Current account/admin principal ve ban durumu |
| Templates | Metadata, renderer key/version | Template lookup ve validation metadata |
| Invitations | Aggregate, working/published içerik, lifecycle, trash, public projection | Invitation access/effective-state policy |
| RSVP | Config, questions, submissions, answers, manage capability | Public submit/update ve Creator results |
| Memories | Guest submission, visibility, hide/delete | Public submit/list ve Creator moderation |
| Gift Registry | Items, reservations, GuestGiftSession | Public reserve/cancel ve Creator management |
| Media | Upload intent, asset lifecycle, provider metadata, purge | Upload/delivery/delete port'ları |
| Plans & Entitlements | Plans, typed values, grants, effective resolver | Action için effective entitlement sonucu |
| Payments | Attempt, iyzico adapter, normalized payment event handler, grant activation | Checkout ve payment state transition |
| Notifications | Email port, outbox, retry | Transactional notification command |
| Integration Foundation | Provider-event inbox ve outbox'ın ortak persistence/claim mekanikleri | Typed inbox/outbox repository ve worker primitive'leri |
| Administration | Aggregate admin queries, settings, audit, ban command | Admin-only operational API |
| Analytics | Aggregate sayaçlar | Invitation aggregate statistics |

Kurallar:

- Invitation bütün invitation-scoped kaynakların ownership root'udur.
- Payments ve Media kendi normalize event/handler'ını ve domain state'ini
  sahiplenir. Integration Foundation yalnız ortak inbox/outbox satırlarının
  persistence, unique claim ve retry mekaniklerini sahiplenir; provider'a
  özgü business kararı vermez.
- Modüller başka modülün EF entity'sini doğrudan değiştirmez; ID ve dar
  application-service sözleşmeleri kullanır.
- Domain provider veya Infrastructure bağımlılığı taşımaz.
- Ortak çekirdek ID, clock, money ve result gibi küçük primitive'lerle
  sınırlıdır.
- Entitlement resolver generic rules engine değildir.

Bağımlılık yönü:

```text
React → HTTP DTO/API
API → Application use cases
API → Infrastructure yalnız composition root/DI registration
Application → Domain
Infrastructure → Application ports + Domain
Domain → provider/framework bağımlılığı yok
```

### 3. Yüksek Seviyeli Entity ve İlişkiler

```text
IdentityUser 0──1 Account
Account 1──* Invitation
Account 1──* AccountPlanGrant
Account 1──* BanRecord

TemplateDefinition 1──* Invitation
Invitation 1──1 WorkingContent
Invitation 0──1 PublishedContent
Invitation 1──* PublicationWindow
Invitation 1──* MediaAsset

Invitation 0──1 RsvpConfiguration
RsvpConfiguration 1──* RsvpQuestion 1──* RsvpQuestionOption
Invitation 1──* RsvpSubmission 1──* RsvpAnswer

Invitation 1──* MemorySubmission 1──* MediaAsset
Invitation 1──* GiftItem 1──* GiftReservation
GuestGiftSession 1──* GiftReservation

Plan 1──* PlanEntitlement *──1 EntitlementDefinition
AccountPlanGrant *──1 Plan
PublicationWindow *──1 AccountPlanGrant

PaymentAttempt 0──1 AccountPlanGrant
WebhookInbox → doğrulanmış provider event
OutboxMessage → güvenilir yan etki
Invitation 1──* AggregateStat
Admin command → AuditLog
```

Creator `IdentityUser` için `Account` zorunlu ve unique'tir. Kontrollü
bootstrap edilmiş Super Admin principal'ı Creator hesabı taşımaz; bu nedenle
genel ilişki `0..1`'dir.

Veri sınırları:

- Ownership, lifecycle, authorization, entitlement, payment, RSVP,
  reservation ve media state relational tutulur.
- Değişken sunum/template içeriği schema-versioned JSONB olabilir ve
  backend typed DTO ile doğrulanır.
- Yalnız bir current `WorkingContent` ve bir current `PublishedContent`
  bulunur; geçmiş revision arşivi oluşturulmaz.
- Autosave working snapshot'a yazar. Active invitation'da explicit
  **Güncelle** published snapshot'ı atomik olarak yeniler.
- Invitation ve düzenlenebilir yönetim kayıtlarında explicit monoton
  revision/concurrency token kullanılır.
- RSVP soruları kalıcı ID taşır. Geçmiş cevapların anlamı, cevap anındaki
  gerekli soru metadata snapshot'ıyla korunur; historical answer cascade
  ile silinmez.
- Payment/audit kayıtları invitation purge cascade'ine bağlanmaz.

### 4. Principal ve Authorization Matrisi

| İşlem | Creator | Guest | Super Admin |
| --- | --- | --- | --- |
| Public Active invitation okuma | Public kullanıcı gibi | `publicCode` ile | Public kullanıcı gibi |
| Draft/working preview | Yalnız owner | Hayır | Hayır |
| Invitation düzenleme/lifecycle | Yalnız owner | Hayır | Hayır |
| RSVP gönderme | Public akış üzerinden mümkün | Modül açık ve invitation erişilebilir ise | Hayır |
| RSVP sonuçlarını görme | Yalnız owner | Hayır | Standard admin yüzeyinde hayır |
| Memory gönderme | Public akış üzerinden mümkün | Effective Active, modül ve entitlement açık ise | Hayır |
| Memory hide/delete | Yalnız owner | Hayır | Standard admin yüzeyinde hayır |
| Gift reserve/cancel | Public akış / kendi capability'si | Effective Active + modül/entitlement; cancel için ayrıca kendi capability'si | Hayır |
| Guest identity/contact görme | Yalnız kendi invitation'ında | Hayır | Standard admin yüzeyinde hayır |
| Plan/settings/ban/audit | Hayır | Hayır | MFA-complete policy |

Creator kuralları:

- `AccountId` request'ten değil authenticated principal'dan alınır.
- Her sorgu/mutation internal resource ID ile birlikte query seviyesinde
  `OwnerAccountId == currentAccount` koşulu taşır.
- Nested ID tek başına authorization değildir.
- `publicCode` Creator/Admin endpoint'lerinde management credential
  değildir.

Super Admin kuralları:

- Ayrı policy ve MFA-complete session gerekir.
- Genel “admin her şeyi görür” bypass policy oluşturulmaz.
- Ban account-level overlay'dir; lifecycle state değildir.
- Ban session'ları geçersizleştirir, public delivery'yi kapatır ve audit
  edilir.

Anonymous mutation policy action-aware'dir:

- Create/increase ve RSVP update gibi kullanımı artırabilen işlemler için
  invitation effective Active, ilgili modül açık, entitlement/quota uygun ve
  gereken capability geçerli olmalıdır.
- Gift cancel gibi mevcut kullanımı azaltan işlem kendi capability'siyle
  yapılabilir; kota aşımı bunu engellemez. Yanıt private veri sızdırmaz ve
  yalnız capability kapsamındaki kaydı etkiler. Bu istisna ban/delete ve
  invitation erişim gate'lerini kaldırmaz.
- Capability hiçbir zaman Creator yetkisine veya başka resource'a genişlemez.
  Boolean entitlement downgrade'ın mevcut içeriğe görünürlük etkisi PD-04
  kapanmadan varsayılmaz.

### 5. Public Invitation ve `publicCode`

- Server-side CSPRNG ile en az 128-bit entropy.
- URL-safe, immutable ve global unique.
- Slug yalnız okunabilir/dekoratif parçadır; lookup'ta authoritative
  değildir.
- Internal ID, account ID veya sıra bilgisi koddan türetilemez.
- Kod parola değildir; linki bilen uygun Active içeriği görebilir.
- Public response yalnız allowlisted projection döndürür.
- Başlangıç zamanı gelmemiş Scheduled, Paused, Expired ve banned account aynı
  PII-free unavailable response'u alır; OG/title/media dahil özel bilgi sızmaz.
- Stored state `Scheduled` olsa bile uygun publication window başlamışsa
  effective state Active kabul edilir. Worker gecikmesi yayını geciktiremez;
  public gate her istekte clock/window/grant kontrolünü senkron yapar.
- Deleted veya bulunmayan code 404 dönebilir.
- Public read ve public mutation API grupları ayrıdır.
- `noindex,nofollow` ve `X-Robots-Tag` uygulanır; bunlar access control
  değildir.
- Lookup ve mutation rate limit uygulanır.
- Expire/reactivation ve slug değişimi publicCode'u değiştirmez.

### 6. Anonymous Capability Modeli

Ortak kurallar:

- Ham token en az 256-bit CSPRNG opaque değerdir.
- Ham token URL, query, analytics veya log'a girmez.
- Browser'da Secure + HttpOnly + uygun SameSite cookie tutulur.
- DB yalnız token purpose + invitation/resource scope ile HMAC digest
  saklar; karşılaştırma constant-time yapılır.
- Capability türleri birbirinin yerine kullanılamaz.
- Unsafe anonymous isteklerde Origin/Referer kontrolü, antiforgery
  bootstrap ve action-specific rate limit birlikte uygulanır.

RSVP:

- İlk başarılı submission sonrasında submission-scoped `rsvp-manage`
  capability üretilir.
- Yalnız ilgili submission'ı read/update eder; listeleme veya başka
  submission erişimi vermez.
- Cookie kaybı yeni/duplicate cevapla sonuçlanabilir; bu kabul edilmiş
  MVP kısıtıdır.

Gift Registry:

- Invitation-scoped `GuestGiftSession` oluşturulur.
- Aynı browser'ın reservation'ları bu session'a bağlanır.
- Token yalnız session'ın reservation'larını iptal edebilir; Creator
  veya başka invitation yetkisi vermez.
- Partial quantity işlemleri transaction/row-lock veya conditional
  update ile toplam miktarı aşamaz.

Memories:

- Guest edit/delete üründe tanımlı olmadığı için kalıcı management token
  yoktur.
- Media içeren pending memory için kısa ömürlü, tek submission'a bağlı
  `memory-upload/finalize` capability verilir.
- Finalize sonrasında guest management yetkisi kalmaz; hide/delete
  Creator yetkisidir.

### 7. Authentication, Google OAuth ve Admin MFA

- ASP.NET Core Identity ve server-managed cookie kullanılır.
- Production auth cookie `__Host-` kurallarına uygun, host-only, Secure,
  HttpOnly, SameSite=Lax ve Path `/` olur.
- Unsafe cookie-auth çağrılarında antiforgery uygulanır.
- Login, MFA ve privilege change sonrasında session rotate edilir.
- Ban/password/security değişiminde security stamp/revocation ile mevcut
  session geçersizleşir.
- Data Protection key ring container restart/redeploy boyunca kalıcı,
  repo dışında ve sınırlı izinlerle tutulur.
- Google authorization code flow kullanılır; framework state,
  correlation ve nonce kontrolleri zorunludur, desteklenen yerde PKCE
  kullanılır.
- Google kimliği provider + immutable `sub` ile bağlanır.
- Aynı email bulundu diye sessiz hesap birleştirme yapılmaz.
- Super Admin public registration veya Google callback ile oluşamaz.
- Admin bootstrap public HTTP endpoint değil, tek seferlik kontrollü
  operasyon/CLI'dır.
- Admin MFA teknik tercihi TOTP authenticator + one-time recovery
  code'lardır; SMS eklenmez.

Deployment sınırı:

- Raw-IP HTTP yalnız private smoke/health içindir.
- Gerçek credential/session trafiği için güvenilir HTTPS zorunludur.
- IP, kodun domain'e bağımlı olmamasını sağlar; TLS gereksinimini
  kaldırmaz.
- Google production login doğrulanmış domain + HTTPS sonrasında açılır.

### 8. Invitation Lifecycle

```text
Draft ──publish now──────────────> Active
Draft ──schedule─────────────────> Scheduled
Scheduled ──start reached────────> Active
Active ──pause───────────────────> Paused
Paused ──resume in valid window──> Active
Active/Paused ──window end───────> Expired
Scheduled ──whole window missed──> Expired
Expired ──new eligible grant─────> Active

Any non-purged state ──delete────> deletedAt overlay
deletedAt after retention────────> Purged
```

Kesin kurallar:

- Soft delete enum state değildir; account ban ayrı overlay'dir.
- Publish required alan, renderer uyumu ve entitlement'ı atomik
  doğrular. Recommended eksikler warning + explicit override'dır.
- `startsAt`/`endsAt` UTC instant olarak tutulur.
- İlk activation veya scheduled başlangıç publication window'u başlatır.
- Pause `endsAt` değerini değiştirmez.
- Public gate her istekte state, clock, deletion, ban ve grant/window
  koşullarını senkron değerlendirir; background job'a güvenmez.
- Job state'i materialize edebilir ancak security boundary değildir.
- Stored state ile effective state ayrıdır: uygun Scheduled window'un
  `startsAt` anı gelince effective state Active, window bittiyse Expired'dır.
  Böylece worker gecikmesi public davranışı değiştirmez.
- Aynı effective-state policy public gate kadar Creator command ve read
  model'larında da authoritative'dir. Stored Scheduled fakat effective Active
  kayıt Active action set'i ve template lock uygular; içerik güncelleme
  semantiği PD-13'e bağlı kalır.
- Active template değişikliği yasaktır; Paused durumda yapılır.
- Preview salt-okunur/simülasyondur; view stats, RSVP, memory veya gift
  kaydı üretmez.
- Restore eski state'i körlemesine public yapmaz.
- Inactive durumda private media delivery de kapanır.
- Purge idempotent/retry-safe'dir; provider media temizliği izlenir.

PRODUCT'ta tanımlanmamış Scheduled cancel/reschedule, Expired→Scheduled
ve kesin restore hedefi kabul edilmiş geçiş gibi uygulanmaz; decision
register kapatılana kadar feature task'ı başlamaz.

### 9. Plan, Grant ve Entitlement

- Desteklenen entitlement key, value type, validation semantiği ve hard
  ceiling kod sözleşmesidir.
- Fiyat ve ticari limit değerleri `PlanEntitlement` olarak DB'dedir.
- `EffectiveEntitlementResolver` account, grant, invitation ve action
  bağlamında tek karar noktasıdır.
- `AccountPlanGrant` kaynağı Free, individual one-time purchase veya
  Organization subscription olabilir.
- Individual grant bir invitation'a atanır.
- Publication başladığında `PublicationWindow.startsAt/endsAt` ve grant
  bağlantısı kalıcılaşır.
- Yeni operasyonlar o andaki effective entitlement ile değerlendirilir.
- Numeric limit düşüşü veri silmez. Read/delete/azaltıcı işlem sürer; yeni
  ekleme kullanım numeric limit altına inene kadar engellenir. Boolean
  entitlement downgrade behavior is defined by PD-04 and follows the accepted
  module-specific behaviors below.
- Payment amount/currency/plan reference ödeme anı snapshot'ı olarak
  immutable tutulur; güncel plan fiyatından yeniden hesaplanmaz.
- Plan/settings değişikliği audit edilir.
- Kota kontrolü application count ile yetinmez; transaction-safe DB
  constraint/lock/conditional mutation gerekir.

### 10. Media Upload ve Delivery Trust Boundary

```text
API authorization
  → private Pending MediaAsset + server-generated provider key
  → kısa ömürlü, tek asset/key upload capability
Browser → Cloudflare Worker (photo) → Images binding WebP re-encoding
  → yalnız transformed output private R2'ye yazılır
Browser → doğrudan Stream (video)
Worker/provider verification → Processing
Verified result → Ready veya Rejected
Public renderer → yalnız Ready + current invitation access gate
```

- Browser MIME, size, duration ve completion beyanı trusted değildir.
- Intent öncesinde actor/capability, invitation state, module,
  entitlement, hard ceiling ve rate limit kontrol edilir.
- Paralel limit bypass'ını önlemek için pending intent'ler quota'ya
  dahildir.
- Cloudflare master credential browser'a verilmez.
- Creator fotoğraf byte'ları doğrudan R2'ye veya hosted Images'a yüklenmez;
  tarayıcı, Images binding ile EXIF/GPS bilgisini temizleyip dönüştürülmüş
  çıktıyı private R2'ye yazan Cloudflare Worker'a upload yapar. Worker ham
  kaynak byte'ları kalıcı olarak saklamaz; Davetiye API/VPS fotoğraf veya
  video byte'larını almaz. Video doğrudan tarayıcıdan Stream'e yüklenebilir.
- API'nin verdiği Worker capability'si kısa ömürlüdür ve tek pending
  asset/key ile upload işlemine bağlıdır. Cloudflare credential'ları
  sunucu tarafında kalır.
- Boyut sınırı yalnız client header'ına veya sonradan HEAD kontrolüne
  bırakılamaz. Worker gerçek stream byte'larını sayıp ceiling aşımında
  yazmayı kesmelidir; sınır aşan intent tüketilir ve oluşmuş partial output
  temizleme kuyruğuna alınarak abuse metriği üretilir.
- R2 yalnız başarıyla normalize edilmiş private pending output'u tutar;
  server-side HEAD/metadata ve güvenli dosya türü doğrulaması sonrası Ready
  olur. Başarısız decode/transform durumunda output R2'ye yazılmaz.
- Images binding için ücretli Cloudflare Images aboneliği gerekir. Cloudflare
  hesap planının Worker request-body sınırı, Worker CPU/bellek sınırları ve
  R2/Images binding erişimi gerçek-provider kabulünden önce doğrulanmalıdır;
  bunlar Worker'ın gerçek stream byte ceiling'inin yerine geçmez. Fatih'in
  2026-10-04 talimatıyla hesap/kaynak kurulumu ve bu limitlerin incelenmesi
  Phase 11 P11-M6/M8'e ertelenmiştir; production media bu doğrulama geçene
  kadar kapalı kalır.
- Stream processing sonucu ve webhook signature doğrulanır.
- SVG/HTML/active content guest upload allowlist'ine girmez.
- Transformation source/variant allowlist kullanılır; arbitrary origin
  transform edilmez.
- Stream playback ve private media delivery signed/gated olur; kalıcı/public
  provider URL dönülmez. Her issuance current invitation access gate'inden
  geçer. Görsel URL'si veya playback-session exchange capability'si en fazla
  60 saniye yaşar. Stream playback session TTL'i izin verilen videoyu yarıda
  kesmeyecek kadar, fakat configurable ve hard-ceiling'li bounded bir süredir.
- Paused/Expired/Banned yeni issuance ve renewal'ı reddeder; mümkünse mevcut
  capability/cache revoke edilir fakat bytes korunur. Trash delivery'yi
  kapatır ve bytes retention boyunca kalır. Yalnız permanent purge/account
  deletion provider object'i fiziksel olarak siler.
- Provider anlık revocation sunmuyorsa asset türüne göre residual exposure,
  gerçek provider davranışıyla ölçülür, belgelenir ve integration test release
  gate'i olur; sınırsız kopyalanmış URL kabul edilmez.
- Provider delete işlemleri idempotent outbox/job ile retry edilir.

### 11. Payment Webhook Trust Boundary

- Checkout plan, amount ve TRY currency değerini server DB'sinden alır;
  browser authoritative değildir.
- API immutable `PaymentAttempt` ve internal correlation/reference
  oluşturur.
- Browser redirect/success yalnız UX sinyalidir; entitlement açmaz.
- iyzico webhook raw request üzerinde resmi production signature
  sözleşmesiyle doğrulanır.
- Provider event/payment reference durable inbox ve unique constraint ile
  replay/idempotency korumasına alınır.
- Amount, currency, merchant/reference, account, plan ve state server
  kaydıyla reconcile edilmeden grant oluşturulmaz.
- Webhook inbox processed state, payment transition ve entitlement grant
  aynı transaction'da tek kez gerçekleşir.
- Email gibi yan etkiler outbox'tan çıkar.
- Out-of-order event state regression oluşturmaz.
- `FakePaymentGateway` production configuration'da startup validation ile
  reddedilir.
- Card data, secret veya tam provider payload loglanmaz/saklanmaz.

### 12. Açık Ürün Kararları — Decision Register

Bu kayıtların kabul durumu satır bazındadır. Faz 1 foundation'ını durdurmaz;
ilgili business feature başlamadan açık kalan kullanıcı kararı gerekir.

| ID | Karar | Bloke ettiği alan | Durum |
| --- | --- | --- | --- |
| PD-01 | Free yayın hakkı hesap başına bir kez mi, yeniden alınabilir mi? | Free grant issuance | **Accepted 2026-10-01:** doğrulanmış hesap başına ömür boyu bir kez; immediate publish'te hemen, Scheduled başlangıcında tüketilir. |
| PD-02 | Organization subscription expiry effect on active windows/public invitations | Subscription lifecycle | Accepted 2026-10-05: at paid-through expiry, stop public access and active publishing; preserve invitation/content data, which can be published again with a future eligible grant. |
| PD-03 | `maxPublishDays` düşüşü başlamış window'u geriye dönük kısaltır mı? | Dynamic entitlement | **Accepted 2026-10-01:** hayır; kabul edilmiş Scheduled/başlamış window korunur, yeni işlemler güncel limite uyar. |
| PD-04 | Boolean entitlement kapanınca mevcut memories/gift/premium-template içeriği public/read-only/hidden seçeneklerinden hangisine geçer? | Module downgrade | **Accepted 2026-10-05:** premium Published snapshot stays pinned; Memories are hidden but preserved and Creator-manageable; Gift list is hidden and new reservations stop, data is preserved for Creator management, and re-enabling restores public visibility. |
| PD-05 | Paused ve Scheduled `maxActiveInvitations` içinde sayılır mı; schedule slot reserve eder mi? | Quota/scheduling | **Accepted 2026-10-01:** çakışan interval kotası; Scheduled aralığı reserve eder, Active/Paused pencere sonuna kadar tutar. |
| PD-06 | Creator ve Guest media aynı kotayı mı paylaşır? | Media entitlement | **Accepted 2026-10-03:** Creator ve Guest medya kotaları ayrıdır. |
| PD-14 | Grant atanmamış Draft davetiyede Creator media intent oluşturulabilir mi? | Phase 4 M2 upload intent | **Accepted 2026-10-03:** hayır; publication window/grant atanana kadar upload intent yok. |
| PD-15 | Gallery için şablon desteği mi, ayrıca Creator modül etkinleştirmesi mi gerekir? | Phase 4 M2/M6 | **Accepted 2026-10-03:** ilgili şablonun `gallery` desteği yeterlidir; ayrı etkinleştirme alanı yoktur. |
| PD-16 | Süresi dolan/reddedilen Creator yüklemesinin provider byte'ları ne zaman silinir? | Phase 4 M7 cleanup/purge | **Accepted 2026-10-05:** yalnızca davetiye kalıcı olarak silindiğinde; expire/reject anında byte silinmez. Provider deletion doğrulanana kadar Rejected/PendingDeletion Creator asset'leri mevcut davetiye item kotasını tutar; yalnız Deleted slot bırakır. |
| PD-07 | Scheduled invitation cancel, reschedule veya hemen yayınlanabilir mi? | Scheduled UX/state | **Accepted 2026-10-01:** üçünü de destekle; cancel → Draft, tüm komutlarda güncel validation/concurrency. |
| PD-08 | Trash restore hedefi Draft mı, önceki effective state mi? | Restore lifecycle | **Accepted 2026-10-01:** daima Draft; otomatik public erişim yok, aynı publicCode korunur. |
| PD-09 | Expired invitation yeni hakla geleceğe Scheduled olabilir mi? | Reactivation | **Accepted 2026-10-01:** yeni grant/window ile immediate veya Scheduled; aynı invitation/publicCode korunur. |
| PD-10 | Gift identity/contact fields and retention policy nedir? | Gift privacy/schema | **Accepted 2026-10-05:** Guest ad-soyadı zorunlu; e-posta ve telefon ayrı ayrı opsiyonel (ikisi, biri veya hiçbiri). Guest iptali/Creator kaldırması tüm rezervasyon kaydını ve kimliği siler; aktif rezervasyonda kişisel veri davetiye kalıcı silinene kadar tutulur. |
| PD-17 | Aktif Guest rezervasyonu olan hediye item'ı Creator silebilir mi? | Phase 7 M2 item delete | **Accepted 2026-10-05:** hayır. Önce aktif rezervasyonlar tek tek kaldırılmalı; her kaldırma tam rezervasyon/kimlik satırını siler ve miktarı serbest bırakır. Item ancak aktif rezervasyon kalmadığında silinebilir. |
| PD-11 | Admin unban yetkisi MVP'de var mı? | Admin lifecycle | **Accepted 2026-10-06:** MFA tamamlamış Super Admin banı kaldırabilir; işlem audit'e yazılır ve erişim geri gelir. |
| PD-12 | Organization renewal/cancel, individual refund ve checkout business akışları nedir? | Commerce UX/domain | **Accepted 2026-10-06 for Phase 8 MVP:** Organization renews monthly until cancellation; cancellation stops the next renewal while access continues through paid-through, then PD-02 applies. Failed renewal charges retry automatically before expiry using provider-configured settings to be verified in Phase 11. Standard/Premium refund requests are manually reviewed through support; there is no in-app or automatic refund flow. A provider-confirmed full refund after support approval revokes the grant, stops publication and preserves invitation data. A final lost chargeback has the same effect; a pending dispute does not change access. Creator may retry canceled or failed one-time checkout; unsuccessful attempts grant nothing. MVP price shows amount and billing period without tax claims; checkout CTA is `Ödemeye geç`. Legal refund/tax terms remain a Phase 11 gate. |
| PD-13 | Scheduled durumdayken autosave edilen içerik başlangıçta otomatik mi yayınlanır, yoksa ayrıca explicit Güncelle mi gerekir? | Invitation snapshots/Creator UX | **Accepted 2026-10-01:** autosave yalnız Working; public snapshot explicit update ile değişir. |
| PD-18 | Account deletion flow and scope | Phase 10 M3 | **Accepted 2026-10-06:** user request plus email verification; on verification revoke sessions/public access immediately and start permanent deletion of the account and owned invitation/guest content without Trash delay; no automatic refund. |
| PD-19 | Exact legal/operational retention periods for payment/audit/log/backup | Phase 10/11 legal gate | **Accepted 2026-10-06:** defer exact durations to Phase 11 legal review; no automatic deletion before that decision. |
| PD-20 | Service vs. marketing consent and optional tracking | Phase 10 privacy UX | **Accepted 2026-10-06:** required service notice; separate default-off marketing opt-in; no optional cookies/tracking in Phase 10. Final wording receives Phase 11 legal review. |
| PD-21 | MVP recovery objective | Phase 10/11 operations | **Accepted 2026-10-06:** RPO ≤24h and RTO ≤8h; restore drill and capacity proof in Phase 11. |
| PD-22 | Account deletion effect on an active Organization renewal | Phase 10 M3 | **Waiting for Fatih:** stop provider auto-renewal immediately at verified deletion or at the paid-through boundary. |
| PD-23 | In-flight one-time payment settles after account deletion starts | Phase 10 M3 | **Accepted 2026-10-06:** preserve provider settlement evidence, do not issue a grant to the deleted account, do not refund automatically; any refund request is manually reviewed through support. |

İlgili UX/integration öncesi kapanacak fakat foundation'ı engellemeyen
noktalar:

- Wizard kesin adım sırası.
- User-facing timezone default'u; teknik instant UTC, timezone IANA ID.
- Google same-email conflict ekranı ve explicit account-linking UI'si.
- Legal consent wording and exact retention durations are deferred to Phase 11; Phase 10 consent scope and interim no-auto-delete policy are accepted in PD-19/PD-20.
- İnteraktif harita provider/credential/consent yaklaşımı.
- iyzico sandbox signature fixture'ları ve production contract sürümü.
- Production domain ve Google/iyzico/Resend/Cloudflare credentials.

### 13. Faz 0 Çıkış Durumu

- MVP kapsamı ve backlog sınırı kayıt altındadır.
- Domain, principal, public ve external provider trust boundary'leri
  kesinleşmiştir.
- Threat model ve kullanıcı yolculukları ayrı dokümanlardadır.
- Açık ürün kararları kabul edilmiş gibi gösterilmemiştir.
- Faz 1 yalnız foundation kapsamıyla başlayabilir; business feature
  implementasyonu Faz 1 kapsamında değildir.
