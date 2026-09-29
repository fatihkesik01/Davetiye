# Faz 0 — Threat Model

Durum: **Accepted baseline**  
Tarih: **2026-09-28**

Kapsam MVP modular monolith, React browser uygulaması, PostgreSQL,
Cloudflare R2/Images/Stream, Google OAuth, iyzico, Resend, Docker Compose
ve paylaşımlı Hostinger VPS'tir.

Severity:

- **Critical:** hesap/platform/ödeme yetkisi ele geçirme
- **High:** tenant aşımı, özel veri ifşası veya ciddi abuse/maliyet
- **Medium:** sınırlı bütünlük, availability veya metadata riski
- **Low:** defense-in-depth hardening

## 1. Korunan Varlıklar

- Creator ve Super Admin credential/session bilgileri
- OAuth state/code, MFA seed ve recovery code'ları
- Draft ve published invitation içeriği
- RSVP cevapları, gift guest adı/iletişim bilgisi ve memories
- Plan, entitlement, ödeme ve subscription kayıtları
- Cloudflare asset/object ID'leri ve upload capability'leri
- Google, iyzico, Resend ve Cloudflare secret'ları
- Sistem ayarları, audit log, PostgreSQL ve off-site backup
- Shared VPS üzerinde Davetiye ile Lora arasındaki izolasyon

## 2. Aktörler

- Anonymous Guest
- Authenticated Creator
- MFA-authenticated Super Admin
- VPS/deployment operator
- Malicious guest/bot
- Malicious veya ele geçirilmiş Creator hesabı
- Internet attacker
- Google, Cloudflare, iyzico ve Resend provider'ları

## 3. Trust Boundary'leri

```text
Browser ↔ Nginx ↔ ASP.NET Core API
Cookie/capability ↔ unsafe API command
API authorization ↔ PostgreSQL/module data
Browser ↔ Cloudflare direct upload
Cloudflare/iyzico webhook ↔ API
API ↔ Google/Resend/Cloudflare/iyzico
Davetiye Compose resources ↔ Lora/shared host
VPS ↔ encrypted off-site backup
```

Her boundary'de dışarıdan gelen kimlik, ID, header, metadata, payload ve
state untrusted kabul edilir.

## 4. Threat Register

| ID | Severity | Tehdit / saldırı senaryosu | Zorunlu mitigation | Release gate |
| --- | --- | --- | --- | --- |
| T01 | Critical | BOLA/IDOR ile Creator başka hesabın invitation/media/RSVP/gift kaydını değiştirir | Query-level `OwnerAccountId`, nested ownership, cross-account negative tests | Authenticated resource API |
| T02 | Critical | Sahte/replayed payment callback entitlement açar | Signed webhook, durable inbox, unique provider reference, amount/currency/order reconciliation, atomic grant | Payment |
| T03 | Critical | Public bootstrap veya zayıf MFA ile Admin takeover | Public endpoint yok, one-time operation, TOTP+recovery, MFA-complete policy, audit | Admin |
| T04 | Critical | OAuth state/account-linking saldırısı | Code flow, state/nonce/correlation, PKCE mümkünse, exact redirect, Google `sub`, silent email merge yok | Google login |
| T05 | High | Cookie theft, fixation veya CSRF | HTTPS, Secure/HttpOnly host-only cookie, rotation/revocation, antiforgery, exact CORS/origin | Authenticated use |
| T06 | High | publicCode management secret gibi kullanılır veya tahmin edilir | 128-bit code, locator-only policy, strict public projection, rate limit | Public API |
| T07 | High | Guest token başka feature/resource için replay edilir | Ayrı 256-bit purpose-scoped capability, HMAC digest, resource scope, revoke/expiry, CSRF | Guest mutations |
| T08 | High | Stored XSS creator/guest content veya OG metadata üzerinden çalışır | React escaping, user HTML yok, URL protocol allowlist, encoding, CSP/security headers | Renderer/public |
| T09 | High | Malicious/oversize media storage veya decoder abuse üretir | Upload-time provider/edge byte ceiling, private Pending state, actual metadata/type decode, active-content reject, partial cleanup, Ready-only | Media |
| T10 | High | Kopyalanmış provider URL Paused/Expired/Trash içeriğini göstermeye devam eder | Private storage, request-time issuance gate, ≤60s image/session-exchange capability, bounded Stream session, renewal denial, access revocation | Media delivery |
| T11 | High | Ban/lifecycle job gecikmesi public içeriği veya session'ı açık bırakır | Synchronous ban/time/delete gate, session revoke, PII-free inactive shell | Invitation/auth |
| T12 | High | Secret veya PII log, image ya da backup'a sızar | Redaction, runtime secrets, private bucket, encrypted off-site backup, retention/access policy | Operations |
| T13 | High | Shared VPS işlemi Lora'yı veya Davetiye izolasyonunu bozar | Ayrı Compose names/network/volume/ports, scoped commands, no Docker socket, Lora verification | Deployment |
| T14 | High | Password reset enumeration/token abuse | Generic response, multi-key rate limit, short single-use token, host-safe URLs, session revoke | Auth |
| T15 | Medium | Gift overbook veya RSVP/media quota race | Transaction/conditional update, locks/constraints, pending intents quota'ya dahil | Guest features |
| T16 | Medium | Bot spam ve storage/provider maliyet saldırısı | Nginx+API route limits, IP+resource/token keys, body caps, orphan cleanup, alerts | Public writes |
| T17 | Medium | Purge DB'yi siler ancak provider media/backup kopyası kalır | Ownership graph, retryable provider delete, reconciliation, backup retention | Deletion |
| T18 | Medium/High | Kötü admin plan değeri maliyet/DoS üretir | Typed feature validation, hard ceilings, audit ve confirmation | Plans/Admin |

## 5. Kimlik, Cookie ve CSRF Baseline

- Production cookie `__Host-` uyumlu, host-only, Secure, HttpOnly,
  SameSite=Lax ve Path `/` olur.
- Tüm authenticated POST/PUT/PATCH/DELETE endpoint'leri antiforgery
  header/token doğrular. JSON Content-Type tek başına savunma değildir.
- Anonymous capability cookie kullanan unsafe endpoint'ler de
  antiforgery/origin kontrolü uygular.
- CORS yalnız runtime-config exact origin listesiyle ve gerektiğinde
  credentials ile çalışır; wildcard kullanılmaz.
- Nginx dışından gelen arbitrary `X-Forwarded-For` trusted değildir;
  yalnız tanımlı local proxy trusted olur.
- Session login, MFA ve privilege change sonrasında rotate edilir.
- Ban, password ve security değişimleri mevcut session'ı revoke eder.
- ASP.NET Data Protection keys kalıcı, repo dışında ve sınırlı izinlidir.
- Raw-IP HTTP gerçek credential trafiğine açılmaz; yalnız private smoke
  ve health kontrolüdür.

## 6. OAuth ve Account Linking

- Google production login verified domain + HTTPS yoksa fail-closed
  biçimde disabled olur.
- Minimal scope: `openid email profile`.
- Issuer, audience, signature, expiry, state/correlation ve nonce
  doğrulanır.
- External identity anahtarı Google `sub` değeridir.
- Unverified veya yalnız eşleşen email ile hesap merge edilmez.
- `returnUrl` yalnız local allowlist içinden seçilir; open redirect yoktur.
- Refresh/access token uygulamanın ihtiyacı yoksa kalıcı saklanmaz.

## 7. Input, Output ve XSS Sınırı

- Creator ve guest içeriği text/data olarak ele alınır; user-authored
  executable HTML/CSS/JavaScript yoktur.
- `dangerouslySetInnerHTML` ürün içeriği için kullanılmaz.
- URL alanlarında protocol allowlist uygulanır.
- DTO field/count/length/numeric range, JSON depth ve body limits server
  tarafında doğrulanır.
- EF parametreli sorgular kullanır; dynamic sort/filter allowlist'tir.
- Server guest tarafından verilen URL'yi fetch etmez; SSRF yüzeyi açmaz.
- CSP en az `object-src 'none'`, `base-uri 'none'` ve kontrollü
  `script-src`; frame policy ve HSTS production'da uygulanır.
- ProblemDetails private stack, SQL/provider payload veya secret içermez.

## 8. Media Güvenliği

- Upload URL bearer capability sayılır ve kısa ömürlüdür.
- Object key server üretir; kullanıcı filename'i path belirlemez.
- Upload byte sınırı provider veya edge upload gateway tarafından stream
  sırasında uygulanır. Sınırsız presigned PUT, yalnız client
  `Content-Length` kontrolü ve yalnız upload-sonrası HEAD yeterli değildir.
- Limit aşımında aktarım kesilir; multipart/partial object abort/delete
  kuyruğuna girer, intent tüketilir ve abuse/cost metriği üretilir.
- R2 CORS yalnız gerekli production/dev origin ve method/header'ları
  içerir.
- Client upload tamamlandı dese bile API provider metadata/HEAD veya
  signed webhook ile doğrulamadan Ready yapmaz.
- Cloudflare Stream webhook raw body signature ve timestamp window ile
  doğrulanır.
- Guest media yalnız ilgili pending Memory capability'sine bağlanır.
- Kalıcı provider URL verilmez. Görsel URL veya playback-session exchange
  capability'si her issuance'da current gate'ten geçer ve en fazla 60 saniye
  yaşar. Stream playback session TTL'i izin verilen videoyu kesmeyecek kadar,
  ancak configurable ve hard-ceiling'li bounded bir süredir.
- Pause/expire/ban yeni issuance ve renewal'ı durdurur; mümkünse mevcut access
  capability/cache revoke edilir, bytes silinmez. Trash delivery'yi kapatır ve
  retention boyunca bytes'ı korur. Fiziksel provider silme yalnız permanent
  purge/account deletion ile yapılır.
- Provider anlık token revocation sunmuyorsa asset-type residual exposure
  gerçek provider davranışıyla ölçülür, belgelenir ve test edilir.
- Pending/orphan intent ve provider object'leri düzenli temizlenir.
- Loglar presigned URL, provider token veya raw webhook body içermez.

## 9. Payment ve Email Güvenliği

- Checkout amount/plan/currency yalnız server kaynağından gelir.
- Browser return URL ödeme kanıtı değildir.
- iyzico signature algoritması kullanılan merchant/API sürümünün resmi
  dokümanı ve sandbox fixture'larıyla kilitlenir.
- Webhook 2xx cevabı yalnız event durable inbox'a alındıktan sonra
  verilir; business processing retry-safe olabilir.
- FakePaymentGateway production'da startup failure üretir.
- Card data veya gereksiz provider PII tutulmaz.
- Verify/reset endpoint'leri account enumeration yapmayan cevap verir.
- Email token'ları kısa ömürlü, single-use ve security state'e bağlıdır.
- Email link base URL'si Host header'dan değil trusted configuration'dan
  üretilir.

## 10. Privacy, Logging ve Retention

- RSVP yalnız Creator'ın configured sorularını; memory opsiyonel adı;
  gift zorunlu adı ve kararlaştırılacak opsiyonel contact'ı toplar.
- Service consent marketing consent'ten ayrıdır.
- Public ve Creator DTO'ları farklı allowlist projection'lardır.
- Loglanmaması gerekenler: Authorization/Cookie/CSRF, OAuth code, MFA
  seed/recovery, reset token, upload URL, webhook raw body, RSVP answer,
  memory text, gift contact ve payment payload.
- Audit append-only, access-controlled ve minimize metadata'lıdır.
- Purge capability digest'leri ve provider media'yı kapsar.
- Backup şifreli/off-site olur; backup retention da deletion/retention
  politikasına tabidir.
- Kesin legal retention ve metinler production öncesi hukuki gate'tir.

## 11. Shared VPS Baseline

- Davetiye ayrı Compose project/container/network/volume adları kullanır.
- Lora port, container, image, volume, network ve Nginx site'ına dokunmaz.
- PostgreSQL internal network'te kalır; API/web yalnız loopback arkasında
  Nginx üzerinden yayınlanır.
- Container'lar mümkün olduğunda non-root, `no-new-privileges`,
  capability drop, read-only filesystem ve resource/log limits kullanır.
- Docker socket mount edilmez.
- `.env` ve backup credential'ları kısıtlı owner/permission taşır.
- Shared-host compromise blast radius'i kabul edilmiş altyapı riskidir.

## 12. Faz 1 Güvenlik Kapıları

1. Principal/public projection/guest capability ADR'ları accepted.
2. Cookie, antiforgery, CORS, proxy ve Data Protection convention'ları.
3. Google config fail-closed; Admin bootstrap ve TOTP/recovery kararı.
4. İki Creator ile cross-account negative integration test pattern'i.
5. DTO validation, ProblemDetails, CSP/header ve log-redaction baseline.
6. Route-class rate limit ve transactional quota convention'ı.
7. Runtime secrets, Compose isolation ve backup/restore runbook.
8. Media intent/finalize ve payment inbox/grant port sözleşmeleri.
9. Gerçek kullanıcı auth'u için HTTPS release gate'i.
