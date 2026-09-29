# Faz 1 — Repository ve Production Foundation Planı

Durum: **Hazır, başlatılmadı**  
Önkoşul: Faz 0 ADR/baseline dokümanlarının kabulü

Faz 1'in amacı çalışan bir production foundation oluşturmaktır. Bu fazda
invitation, RSVP, memories, gift, payment veya kapsamlı Creator/Admin UI
business feature'ları uygulanmaz.

## Sıralı Task Listesi

| No | Task | Bağımlılık | Ana roller | Kabul ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Faz 0 decision index ve açık product decision kayıtlarını onayla | Yok | Architect, Reviewer | ADR'lar çelişkisiz; PD-01–PD-13 accepted görünmüyor |
| 2 | Repository/solution klasörlerini ve dependency kurallarını oluştur | 1 | Architect, Backend | Boş yapı build; dependency yönü dokümana uygun |
| 3 | ASP.NET Core Api/Application/Domain/Infrastructure ve test projelerini initialize et | 2 | Backend | Proje referansları yalnız izin verilen yönde |
| 4 | React + TypeScript uygulama ve test altyapısını initialize et | 2 | Frontend | Production build veren feature'sız shell |
| 5 | Local/test PostgreSQL ve strongly typed config/secrets validation kur | 3 | Database, Backend, Security | Secret commit yok; DB connectivity/health çalışıyor |
| 6 | API baseline: `/api/v1`, ProblemDetails, validation, correlation, OpenAPI, live/ready health | 3,5 | Backend | Contract/error/health integration smoke yeşil |
| 7 | DB convention: UUID, UTC, money, revision, soft-delete, naming ve migration ownership | 3,5 | Database | Convention ADR'a uygun; migration yolu app startup'tan ayrı |
| 8 | Identity/Account, typed Plan/Entitlement/SystemSetting foundation şemasını hazırla | 7 | Backend, Database, Security | MVP single-owner account; hard-ceiling/value-type sınırı testli |
| 9 | Webhook inbox, outbox ve background job persistence foundation'ı oluştur | 7 | Backend, Database | Unique/idempotent claim ve retry-safe worker testi |
| 10 | Public/Creator/Admin route shell, generated API client ve Türkçe i18n temeli | 4,6 | Frontend, UI/UX | Feature'sız shell responsive/a11y smoke geçiyor |
| 11 | Cookie/antiforgery/CORS/proxy/header/rate-limit/Data Protection security baseline | 6 | Security, Backend | Security integration testleri; raw HTTP auth production'da yok |
| 12 | Controlled Admin bootstrap ve TOTP/recovery foundation sözleşmesi | 8,11 | Backend, Security | Public bootstrap route yok; MFA-complete policy testli |
| 13 | Authorization test harness: iki Creator ve foreign-resource negative pattern | 8,11 | Tester, Backend, Security | BOLA negative test pattern CI'da çalışıyor |
| 14 | Architecture tests ve PostgreSQL Testcontainers harness | 3,5,7 | Tester, Backend, Database | Layer violation ve gerçek PostgreSQL testi CI'da |
| 15 | Dockerfile, Compose ve Nginx IP-first/private-smoke foundation | 3,4,5,11 | Backend, Security | Davetiye kaynakları Lora'dan ayrı; DB public değil |
| 16 | CI: backend/frontend build, lint/typecheck/test, migration test ve dependency/container scan | 14,15 | Tester, Backend, Frontend | Temiz checkout pipeline yeşil |
| 17 | Structured logging, redaction, health monitoring ve audit baseline | 6,11 | Backend, Security | Secret/PII içermeyen smoke log |
| 18 | Daily off-site PostgreSQL backup ve restore runbook taslağı | 5,15 | Database, Security | Restore adımları ve failure monitoring tanımlı |
| 19 | UX foundation deliverable'ları: route matrix, low-fi shell, lifecycle/async-state microcopy | 10 | UI/UX, Frontend | PHASE_0/UX sınırlarına uyumlu ve feature içermiyor |
| 20 | Faz 1 independent review ve Faz 2 readiness raporu | 1–19 | Reviewer, Architect, Security, Tester | Kritik bulgu yok; açık product kararları yeniden listeli |

## Teknik Alt Görevler

### Backend/API

- Central configuration/options validation
- Environment ayrımı ve production fail-closed kuralları
- Request/correlation ID
- Sanitized ProblemDetails
- OpenAPI source of client contracts
- Trusted proxy ve request body limits
- Health liveness/readiness ayrımı
- Provider port contract'ları; gerçek business entegrasyonu yok

### Database

- Tek `DbContext` ve tek migration assembly
- Migration runner app startup'tan ayrı
- Empty DB migrate ve upgrade test strategy
- Explicit revision convention
- Transactional inbox/outbox claim strategy
- Generic inbox/outbox persistence Integration Foundation'a; provider-specific
  handler ve state transition owning modüle aittir
- Destructive migration review ve expand/contract standardı
- Seed işlemleri idempotent; ticari değerler application constant değil

### Frontend

- Public/Creator/Admin lazy route zones
- Feature içermeyen responsive shells
- API client generation
- Global error/loading/not-found boundaries
- Türkçe i18n initialization
- Accessibility primitives ve focus strategy
- Auth/guest secret localStorage'a yazılmaz

### Security/Operations

- HTTPS gerçek-auth release gate
- Host-only cookie ve antiforgery convention
- Persistent Data Protection keys
- Exact CORS origins ve forwarded-header trust
- Route-class rate limit policy
- Secret/log redaction policy
- Non-root/container isolation mümkün olan servislerde
- Lora read-only post-deploy verification checklist
- Shared VPS'te host-wide Docker prune yasağı; temizlik yalnız Davetiye
  Compose kaynakları ve açıkça adlandırılmış image'larla sınırlıdır

## Faz 1 Dışında Kalanlar

- Invitation CRUD ve renderer feature'ları
- Template tasarımlarının üretimi
- RSVP, Memories ve Gift business akışları
- Cloudflare gerçek upload entegrasyonu
- iyzico checkout/webhook implementasyonu
- Resend production gönderimi
- Creator/Admin kapsamlı ekranları
- Production deployment ve canlı kullanıcı trafiği

## Definition of Done

- Temiz checkout backend ve frontend build eder.
- Gerçek PostgreSQL integration harness CI'da çalışır.
- Katman bağımlılıkları architecture test ile korunur.
- Secret repository/image/frontend bundle içinde değildir.
- Security baseline testleri geçer.
- Feature'sız üç shell responsive ve temel erişilebilirlik smoke testini
  geçer.
- Compose kaynakları Lora ile isim/port/volume/network paylaşmaz.
- Backup/restore ve deployment runbook taslakları vardır.
- Faz 0 açık ürün kararları yanlışlıkla implementation kararı olarak
  kapatılmamıştır.
