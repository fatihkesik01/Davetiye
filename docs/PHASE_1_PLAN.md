# Faz 1 — Repository ve Production Foundation

Durum: **Tamamlandı ve doğrulandı (17/17 milestone birimi); UX Foundation sözleşmesi Accepted (Reviewer 2026-09-29). Sonraki fazlar (2–4) tamamlandı; güncel durum için `docs/ROADMAP.md` ve `docs/AI_HANDOFF.md`.**
Başlangıç tarihi: **2026-09-28**
Bağımlılık: **Faz 0'ın (`docs/PHASE_0_PLAN.md`) ve ilgili ADR'ların kabulü**

Bu belge, diğer tüm `PHASE_N_PLAN.md` dosyalarıyla aynı format ve bölüm
sırasını kullanır (bkz. `docs/PHASE_TEMPLATE.md`). Faz 1 tamamlanmış ve
doğrulanmış olduğu için aşağıdaki bölümler hem plan hem de gerçekleşen
sonucu özetler; ayrıntılı kanıt (checkpoint, milestone başına test/finding
kaydı) ve kabul edilmiş UX Foundation sözleşmesinin tam metni, kaynak kod
yorumlarındaki (`docs/PHASE_1_PLAN.md §N`) referansların geçerli kalması
için orijinal numaralandırmasıyla belge sonunda Ek olarak durur (2026-09-29
öncesi bu, sırasıyla `PHASE_1_EXECUTION.md` ve `PHASE_1_UX_FOUNDATION.md`
adlı iki ayrı dosyaydı; faz dosyalarının hepsi aynı formatı kullansın diye
birleştirildi, içerik kaybolmadı).

## Amaç

Business feature'lar (invitation, RSVP, Memories, Gift, payment) implement
edilmeden önce; çalışan bir repository/build/test/CI/container temeli,
Identity/Account ve typed Plan/Entitlement/SystemSetting şema temeli,
generic inbox/outbox, API/security baseline, Admin bootstrap/MFA temeli ve
feature'sız Public/Creator/Admin shell'leri kurmak.

## Kapsam

Repository/build temeli; feature'sız React ve ASP.NET Core shell'leri;
PostgreSQL/config/migration/test altyapısı; API ve security baseline;
Identity/Account ile typed plan/settings şema temeli; generic inbox/outbox;
Admin bootstrap/MFA temeli; CI; container/Compose/Nginx temeli;
observability; backup/restore runbook'ları; UX foundation sözleşmesi (route/
shell/authorization matrisi, autosave/lifecycle/error microcopy modeli,
responsive/WCAG contract'ı — bkz. Ek §10). Ayrıntılı milestone listesi
aşağıdaki Milestone'lar tablosundadır.

## Kapsam Dışı

Invitation CRUD/renderer ve template tasarımları; RSVP, Memories, Gift
business akışları; gerçek Cloudflare upload; iyzico checkout/webhook;
Resend production gönderimi; kapsamlı Creator/Admin ekranları; production
deployment ve canlı kullanıcı trafiği. Backlog özellikleri de eklenmez.

## Bağımlılıklar

Faz 0 ADR/baseline dokümanlarının kabulü (`docs/PHASE_0_PLAN.md`).

## Bu Fazı Bloke Eden Açık Ürün Kararları

PD-01–PD-13'ün tamamı bu faz tarafından kapatılmaz (Ek §6). Foundation
şemaları bu kararlardan birini dolaylı olarak kesinleştiremez; ilgili
business milestone başlamadan kullanıcı kararı gerekir.

## Milestone'lar

Owner ve Bulgu sütunları `docs/AI_WORKFLOW.md` §13.3 attribution kuralı
gereği tutulur — bir sorumluluk kaydıdır, ölçülmüş bir efor/iş yükü yüzdesi
değildir. Ayrıntılı kanıt (checkpoint, test sonuçları, finding detayı) için
Ek §3'e bakın. 2026-09-29'da, `docs/AI_WORKFLOW.md` §4'ün "ilgili işleri
birleştir, aşırı bölme" kuralına uygun olarak orijinal 17 birimlik (M0,
M1, M1U, M2A, M2B, M3, M4, M5A, M5B, M6a, M6b, M7A, M7B, M8, M9, M10, M11)
tablo 7 gruba konsolide edildi — içerik/kanıt kaybolmadı, Ek §3'teki her
alt bölüm orijinal M-kodlarıyla hâlâ ayrı ayrı okunabilir.

| No | Milestone (kapsadığı birimler) | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü | Durum |
| ---: | --- | --- | --- | --- | --- |
| 1 | Foundation ve decision guard (M0, M1, M1U) | Yok | Architect, UI/UX | ADR index tutarlı, PD-01–PD-13 unresolved; repository/dependency topology ADR-0001 ile uyumlu; UX foundation sözleşmesi Accepted | Tamamlandı — M0/M1 owner kayıtlı değil; M1U Reviewer: 0 blocking, 9/9 deliverable yeterli, 2 Low açık (microcopy etiketi, ban-confirmation örneği eksik) |
| 2 | Backend/Frontend/DB skeleton (M2A, M2B, M3) | 1 | Kayıt yok (2026-09-28 takeover'da bağımsız doğrulandı) | Temiz build, feature'sız production shell, gerçek PostgreSQL harness ve ayrı migration runner | Tamamlandı, doğrulandı |
| 3 | API + Identity/Settings + Integration Foundation (M4, M5A, M5B) | 2 | Backend, Database | `/api/v1`/ProblemDetails/health smoke; Identity/Account/typed settings schema; idempotent inbox/outbox claim | Tamamlandı, doğrulandı — Reviewer+Security 0 Critical/High üçünde de; M4 2 Medium+3 Low açık, M5A 3 Medium açık, M5B 1 Medium bulundu ve düzeltildi + 2 Low açık |
| 4 | Security/Admin auth foundation (M6a+M6b) | 3 | Backend | Cookie/CSRF/proxy/rate-limit/DP/MFA, Google fail-closed ve raw-HTTP auth gate testleri | Tamamlandı, doğrulandı — 0 current Critical; birkaç Medium/Low bulundu ve düzeltildi (bkz. Ek §3); 1 future feature-activation prerequisite kaydedildi (ban session revocation), 2 Low açık |
| 5 | Frontend shells + route guards (M7A, M7B) | 1, 4 | Frontend, Orchestrator | Responsive/a11y shells, real-OpenAPI drift check, Public/Creator/Admin guard matrisi | Tamamlandı, doğrulandı — Tester 4/4+8/8 ve 13/13 yeşil; Reviewer/Security Critical/High yok, 2+2 Low açık |
| 6 | Doğrulama ve altyapı (M8, M9, M10) | 3, 4, 5 | Tester, Orchestrator | Test-only BOLA fixture; gerçek container runtime/Compose smoke/restore rehearsal; clean-checkout CI | Tamamlandı, doğrulandı — Architecture 66/66, Unit 96/96; restore rehearsal başarılı; [GitHub Actions run 36534834602](https://github.com/fatihkesik01/Davetiye/actions/runs/36534834602) yeşil |
| 7 | Faz kapanışı (M11) | 1–6 | Orchestrator | Reviewer/Security/Tester/Architect onayı ve Phase 2 readiness | Tamamlandı, doğrulandı |

## Milestone Bağımlılık Grafiği

M1 → M2 → M3 → M4 → M5 → M6 → M7 (kapanış). Gerçekte bu yeni M2 içindeki
(M2A, M2B) ve yeni M3 içindeki (M4, M5A, M5B) alt işler dikkatli biçimde
paralel yürütüldü — aynı `DbContext`/migration dosyalarına yazım Database
specialist'in merge sırasıyla serialize edildi (bkz. Ek §3 detay kaydı).

## Beklenen Uzman Rolleri

Architect, Backend, Database, Frontend, UI/UX, Security, Tester, Reviewer —
tümü en az bir milestone'da gerçek rol aldı (bkz. Milestone'lar tablosu
Owner sütunu).

## Doğrulama Kriterleri

Temiz checkout backend ve frontend build eder; gerçek PostgreSQL
Testcontainers integration harness CI'da çalışır; katman bağımlılıkları
architecture test ile korunur; secret repository/image/frontend bundle
içinde değildir; security baseline testleri geçer; feature'sız üç shell
responsive ve temel erişilebilirlik smoke testini geçer; Compose kaynakları
Lora ile isim/port/volume/network paylaşmaz; backup/restore ve deployment
runbook taslakları vardır. Gerçekleşen sonuç: local Release integration
43/43, unit 96/96, architecture 66/66, frontend Vitest 13/13; GitHub
Actions clean-checkout gate [run 36534834602](https://github.com/fatihkesik01/Davetiye/actions/runs/36534834602)
yeşil.

## Faz Tamamlanma Kriterleri

M0–M11 (M1U ve split M7 dahil) tamamlandı ve doğrulandı; unresolved
Critical/High bulgu yok; her Medium bulgu için owner/risk/disposition kaydı
var (Ek §3, mileston başına detay); PD-01–PD-13 açık durumuyla yeniden
raporlandı; Compose/Nginx işleri yalnız local/test foundation olarak
hazırlandı, gerçek VPS işlemi yapılmadı (Ek §7 Deployment Guard).

## Handoff Gereksinimleri

`docs/AI_HANDOFF.md`, Faz 1'in tamamlandığını, Faz 2'nin onaylandığını,
bilinen teknik borcu ve PD-01–PD-13'ün unresolved kaldığını kaydeder.

## Sonraki Faz Planlama Kapısı

Bu fazın tamamlanması, Faz 2'nin implementasyonuna otomatik izin vermez;
Faz 2 yalnız Fatih'in 2026-09-29'daki açık onayıyla başlayabilir
(`docs/PHASE_2_PLAN.md`).

---

## Ek: Detaylı İçerik (orijinal numaralandırma korunmuştur)

Aşağıdaki bölümler, kaynak kod yorumlarındaki (`docs/PHASE_1_PLAN.md §N`
biçimindeki) referansların ve milestone kanıt izinin geçerli kalması için
orijinal `§` numaralandırmasıyla korunmuştur; yukarıdaki özet bölümler bu
içeriğin tekrarı değil, ona giriş/işaretçidir.

### 1. Karar Önceliği ve Kapsam Koruması

Çelişkide uygulanacak öncelik sırası `docs/AI_WORKFLOW.md` §3'te tanımlıdır
(PRODUCT.md → kabul edilen ürün kararları → accepted ADR'lar →
ARCHITECTURE.md → phase baseline/plan → specialist teknik önerileri). Bu
faz o sıra kullanılarak yürütülmüştür.

Phase 1'e dahil olanlar: repository ve build temeli, feature'sız React ve
ASP.NET Core shell'leri, PostgreSQL/config/migration/test altyapısı, API ve
security baseline, Identity/Account ile typed plan/settings şema temeli,
generic inbox/outbox, Admin bootstrap/MFA temeli, CI, container/Compose/Nginx
temeli, observability ve backup/restore runbook'larıdır.

Phase 1 dışında kalanlar: invitation CRUD/renderer ve template tasarımları,
RSVP, Memories, Gift, gerçek Cloudflare upload, iyzico checkout/webhook,
Resend production gönderimi, kapsamlı Creator/Admin ekranları, production
deployment ve canlı kullanıcı trafiğidir. Backlog özellikleri de eklenmez.

### 2. Başlangıç Repository Checkpoint'i

Checkpoint: `2026-09-28`, HEAD
`4041a31098976be7e989e33a827ae16ee783725e`.

- Application, solution, React/.NET project veya migration yoktu.
- Modified tracked: `.codex/config.toml`, sekiz mevcut specialist agent
  config'i, `AGENTS.md`, `docs/PRODUCT.md`.
- Untracked: `.codex/agents/orchestrator.toml`, `docs/ARCHITECTURE.md`,
  `docs/DEPLOYMENT.md`, `docs/PHASE_0_BASELINE.md`, `docs/PHASE_1_PLAN.md`,
  `docs/THREAT_MODEL.md`, `docs/UX_FLOWS.md` ve `docs/adr/`.
- Bu değişiklikler kullanıcıya aittir; milestone çalışmaları bunları geri
  almayacak veya ilgisiz biçimde yeniden yazmayacaktır.
- Araç görünümü: .NET SDK `10.0.401`, Node `22.17.0`, npm `11.4.2` ve Docker
  client `28.1.1` görülebilir durumdaydı. Docker daemon erişilebilir değildi.
  Bu kayıt framework/target sürümü kararı vermez.

### 3. Milestone Kanıtları ve Detaylı Kayıt

Özet Milestone'lar tablosu (No/Bağımlılık/Sorumlu/Tamamlanma/Durum) belgenin
başındaki "Milestone'lar" bölümündedir. Aşağıdaki alt bölümler o tablonun
dayandığı ayrıntılı kanıt kaydıdır (checkpoint, takeover reconciliation ve
milestone başına test/finding detayı).

#### Takeover reconciliation (2026-09-28)

A new session picked up the repository mid-phase after a prior session's
token budget ended. Per `docs/AI_WORKFLOW.md` §1/§11 it independently
re-verified the claimed state (build, architecture/unit/integration tests,
real PostgreSQL) rather than trusting the prior handoff; the state was
accurate except one previously-recorded blocker that was already fixed in
the working tree. M0–M3 were confirmed complete; M4 onward continued from
there.

#### M7A — Generated client ve feature'sız route shells (completed 2026-09-29)

`src/web/`: lazy-loaded Public/Creator/Admin route zones, Turkish
feature-less placeholders, global loading/not-found/error boundaries, and
skip-link/landmark/focus a11y primitives — no business data or
client-side authorization boundary. Generated OpenAPI client
(`npm run api:sync`/`api:verify`/`api:generate`/`api:check`) keeps the
cookie-based client byte-verified against the live backend contract.
Independent Reviewer: 0 Critical/High/Medium after one Medium
generated-contract fix.
**Açık teknik borç (Low):** malformed/nested route fallback and
route-focus behavior not yet explicitly tested; browser-level 320 px/200%/
keyboard/a11y evidence deferred to M7B/M8 (not a gap that justified
building business UI early).

#### M4 — API runtime contract (completed 2026-09-28)

Backend: `/api/v1` prefix, sanitized RFC7807 ProblemDetails (no leak
outside Development), allowlisted correlation ID, OpenAPI generation,
genuinely separate `/health/live`/`/health/ready` (the latter checks real
PostgreSQL), Kestrel body-limit hard ceiling, fail-closed forwarded-header/
trusted-proxy config (loopback-only default). Independent Reviewer and
Security passes: 0 Critical/High.
**Açık teknik borç (Medium, owner Backend/Security):** trusted-proxy
default lives in the environment-agnostic base config, not an
environment-scoped file — confirm this is the intended production
posture; `GlobalExceptionHandler` echoes the raw request path in
ProblemDetails `Instance` — revisit before any sensitive path segment
(e.g. a token) exists.
**Açık teknik borç (Low):** `/openapi/v1.json` has no environment guard
(no secret leak today, but M6 should decide prod exposure explicitly); no
end-to-end test proves Kestrel's 413 body-limit rejection or that a
forged `X-Forwarded-For` from an untrusted source is ignored (both
unit-tested only).

#### M5A — Identity/Account/typed Plan/Entitlement/SystemSetting schema (completed 2026-09-28)

Database (per Backend's conventions): ASP.NET Core Identity in the single
`DavetiyeDbContext` (no Roles table — Creator vs. Super Admin is a
bootstrap concern per ADR-0002), `Account` (one per Identity user, DB
unique constraint), `BanRecord` (schema only), `EntitlementCatalog`/`Plan`/
`PlanEntitlement`/`AccountPlanGrant`/`SystemSetting`. No auth endpoints,
resolver or audit logging — correctly deferred. Independent Reviewer and
Security passes: 0 Critical/High; the "no FK on `AccountPlanGrant`, no
Roles table" design calls both confirmed sound.
**Açık teknik borç (Medium, owner Database/Backend):** before any real
grant issuance, validate `AccountPlanGrant.AccountId` through a narrow
Identity contract (no DB-level FK by design); before Plan seed data is
finalized, tighten `EntitlementCatalog`'s oversized media hard ceilings;
before any Account hard-delete path is built, reconsider `BanRecord`'s
cascade-delete (audit history should likely survive it).

#### M5B — Inbox/outbox worker primitives (completed 2026-09-28)

Backend (Integration Foundation, provider-neutral per
`docs/PHASE_0_PLAN.md` §2): `InboxMessage`/`OutboxMessage`, a DB-level
unique `(ProviderName, ProviderEventId)` idempotency constraint, an atomic
`FOR UPDATE SKIP LOCKED` claim primitive, and a generic
`BatchMessageWorker<TMessage>` — no real handler, no provider name
anywhere in production source. Independent Reviewer and Security passes:
0 Critical/High. **One Medium defect found and fixed:** a bare `catch`
mis-recorded cooperative batch cancellation as a handler failure; a
specific `OperationCanceledException` guard was added and re-verified.
**Açık teknik borç (Low, owner: first real payload writer):** `Payload`
columns are unbounded `text` with no redaction hook — the first real
handler must bound/minimize it.

#### M6a — Core email/password auth + cookie/CSRF/CORS/rate-limit/Data Protection baseline (completed 2026-09-28)

M6 was split into M6a (this) and M6b (Google/Admin/MFA). Delivered:
`AddIdentityCore` wiring, `__Host-` production cookie,
`SecurityStampValidator` revoking sessions on password reset, antiforgery,
exact-origin CORS, per-IP rate limiting on register/login/reset-request,
persistent gitignored Data Protection keys, `IEmailSender` with a
Development-only sender and a fail-closed production default, full auth
endpoints with ban-check and required email confirmation, and a raw-HTTP
fail-closed proof for non-HTTPS Production traffic. Independent Reviewer
and Security passes found and the same pass fixed several issues (missing
`RequireConfirmedAccount`, a non-gitignored key directory, raw token
logging, an under-tested CSRF exemption) — all re-verified closed.

**Future feature-activation prerequisite (not an open Phase 1 finding —
record now so it isn't lost):** creating a `BanRecord` does not revoke an
already-issued session today (no ban-creation endpoint exists yet). The
milestone that first adds one **must** call
`UserManager.UpdateSecurityStampAsync` in the same operation, mirroring
`AuthAccountService.ResetPasswordAsync` — otherwise banning is a no-op
against active sessions the instant that endpoint ships
(`docs/THREAT_MODEL.md` T11).

**Açık teknik borç (Medium):** production email adapter must reorder or
compensate `RegisterAsync`'s commit-before-send sequencing (owner: real
email adapter milestone); `/auth/confirm-email`, `/auth/reset-password`,
and the antiforgery-token endpoint carry no rate limiting, unlike
register/login (owner: Security/Backend); the M4 forwarded-header e2e-test
gap remains unmet.
**Açık teknik borç (Low):** login and password-reset-request both have
minor timing side-channels (account-enumeration signal), not required by
an explicit threat-model item.

#### M6b — Google OAuth + Admin bootstrap + TOTP/MFA (completed 2026-09-29)

Backend, on M6a's foundation: Google authorization-code sign-in (PKCE,
`sub`-keyed, no silent merge, fail-closed without verified HTTPS), a
controlled one-time Admin bootstrap console tool that cannot create a
Creator `Account`, TOTP MFA + single-use recovery codes via ASP.NET Core
Identity's built-in provider, and a reusable `MfaComplete` policy proven
through a real endpoint + middleware. Independent Reviewer and Security
passes found and the same pass fixed: missing `email_verified`
enforcement (reopened the email-squatting risk M6a closed for password
login), a missing ban check on the Google existing-identity fast path
(dormant High, fixed immediately — the check pattern already existed), and
missing TOTP-enrollment lockout/rate-limit.
**Açık teknik borç (Low, owner: next touch):** `ResolveSafeReturnUrl` is
safe today (relative-path-only heuristic) but not the literal allowlist
ADR-0002/threat-model text describes — revisit if touched again;
`CompleteSignInAsync`'s `bypassTwoFactor: true` relies on a code-unenforced
"Google-linked identity is never Super Admin" invariant that account-linking
work must re-verify before relying on it (tracked as required M1 scope in
`docs/PHASE_2_PLAN.md`).

M5A/M5B may be designed in parallel, but `DbContext`/EF configuration/
migration file changes are fully serialized through the Database
specialist's merge order.

#### M1U — UX foundation contract (Accepted 2026-09-29)

Independent Reviewer pass: all 9 required deliverables present and
substantive, no scope violation, no open product decision assumed, no
contradiction with PRODUCT/UX_FLOWS/`docs/PHASE_0_PLAN.md`. **Açık teknik
borç (Low):** microcopy/state tables don't carry the same
`[Gereksinim]/[Öneri]` tags as the prose sections; no ban-confirmation
copy example exists (low priority — Admin is placeholder-only in Phase 1).

UI/UX produced, shell-code öncesi: (1) Public/Creator/Admin route ve
authorization matrisi; (2) mobile-first Creator shell/editor IA; (3) tek
creation-entry sözleşmesi (kesin wizard sırası yok); (4) autosave/offline/
conflict/Active-update microcopy modeli; (5) lifecycle confirmation/empty/
inactive/error matrisi (PD-07/08/09/13 varsayılmaz); (6) publish preflight
ve immediate/scheduled low-fi; (7) RSVP/Memories/Gift iki taraflı akış
şemaları (business UI yok, PD-10 açık); (8) responsive/WCAG 2.2 AA
component/focus/live-region sözleşmesi; (9) 320 px/200%/keyboard/route-
focus/async-announcement E2E/a11y acceptance matrisi. M7A bu sözleşmeyi
shell düzeyinde uygular; M7B, M6 sonrası auth route guard'larını aynı
matrisle doğrular.

### 4. Repository Topology ve Dosya Sahipliği

Planlanan temel yerleşim:

```text
src/
  backend/
    Davetiye.Api/
    Davetiye.Application/
    Davetiye.Domain/
    Davetiye.Infrastructure/
  web/
tools/
  Davetiye.DatabaseMigrator/
tests/
  backend/
    Davetiye.UnitTests/
    Davetiye.IntegrationTests/
    Davetiye.ArchitectureTests/
deploy/
  nginx/
  scripts/
docs/
.github/workflows/
```

- Architect: architecture/execution belgeleri, module boundary contract ve
  M11 boundary review.
- Backend M2 scaffold owner: repository kökündeki `.sln`, `global.json`,
  `Directory.Build.*`/`Directory.Packages.props` ve backend project files.
  Bu ortak dosyaları milestone boyunca yalnız atanmış scaffold owner değiştirir.
- Database: `DbContext`, relational conventions, tek migration assembly ve
  migration/backup scripts ve `tools/Davetiye.DatabaseMigrator/`. Backend ile
  ortak persistence/DI dosyalarında sıralı handoff yapar.
- Frontend: `src/web/` ile buradaki `package.json`, lockfile, TypeScript,
  bundler, lint ve test config dosyalarının tek sahibidir. UI/UX agent
  davranış/a11y contract'ını review eder; aynı dosyaları eşzamanlı düzenlemez.
- Her implementer kendi değişikliğinin unit/integration/component testlerini
  sahiplenir. Tester shared test harness, E2E/a11y acceptance, `.github/workflows/`
  CI orchestration ve bağımsız doğrulamayı sahiplenir; production code
  düzeltmesini owning specialist'e yönlendirir.
- Security ve Reviewer: bağımsız bulgu/review; ortak production dosyalarında
  implementation agent'ıyla eşzamanlı düzeltme yapmaz.
- Backend, Security ve Database `deploy/` değişikliklerini tek bir atanmış
  dosya sahibi üzerinden koordine eder.

#### Migration mekanizması

- `Davetiye.Infrastructure` tek EF Core migration assembly'sidir; migrations
  `src/backend/Davetiye.Infrastructure/Persistence/Migrations/` altında kalır.
- `tools/Davetiye.DatabaseMigrator/` yalnız Infrastructure migration assembly
  ve strongly typed runtime config kullanan, tek seferlik .NET console
  executable'dır. Resident servis veya ikinci ASP.NET application değildir.
- API startup hiçbir ortamda `Database.Migrate`/auto-migrate çağırmaz.
- Local, CI ve daha sonraki deployment akışı migration'ı açık bir önceki adım
  olarak bu executable ile çalıştırır; başarısız migration API promotion'ını
  durdurur.
- Database specialist migration/runner sahibidir; Backend yalnız shared config
  ve DI contract'ında koordine olur. Empty DB ve upgrade-path testleri runner'ı
  gerçek PostgreSQL üzerinde çağırır.

### 5. Dependency ve Runtime Sınırı

İzin verilen yön:

```text
React -> HTTP DTO/API
API -> Application -> Domain
Infrastructure -> Application ports + Domain
API -> Infrastructure yalnız composition root/DI registration
DatabaseMigrator -> Infrastructure yalnız migration/config execution
```

- Domain framework veya provider bağımlılığı taşımaz.
- Endpoint/business logic Infrastructure tiplerine bağlanmaz.
- Modüller başka modülün entity/table'ını doğrudan mutate etmez; ID ve dar
  application-service contract'ları kullanır.
- Mimari tek responsive React application, tek ASP.NET Core process, tek
  PostgreSQL database, tek `DbContext` ve tek migration assembly'dir.
- Ayrı microservice/database, message broker ve Kubernetes MVP bağımlılığı
  değildir.

#### Test edilebilir modular-monolith konvansiyonu

- Her katmanda namespace/folder biçimi
  `Davetiye.<Layer>.Modules.<ModuleName>` / `Modules/<ModuleName>/` olur.
- Domain entity/value object'leri owning modül altında kalır. Başka modül bu
  entity tipini veya EF configuration'ını referans alamaz; cross-module ilişki
  ID ve dar Application public contract'ı ile kurulur.
- Cross-module çağrı yüzeyi yalnız
  `Davetiye.Application.Modules.<ModuleName>.Contracts` altında bulunur;
  implementation/internal handler tipleri bu yüzeye eklenmez.
- EF entity configuration ve tablolar modül klasöründe sahiplenilir; başka
  modülün tablosuna doğrudan mutation veya cross-module EF navigation yoktur.
- `IntegrationFoundation`, Application'da generic inbox/outbox contract'larını,
  Infrastructure'da persistence/claim/worker primitive'lerini barındırır;
  provider-specific handler veya business state transition sahiplenmez.
- `SharedKernel` yalnız allowlisted ID, clock, money ve result gibi küçük
  primitive/abstraction'larla sınırlıdır; business aggregate, provider DTO'su,
  repository veya genel helpers çöplüğü içermez.
- Architecture test projesi M2A ile başlar ve namespace/project bağımlılığı,
  cross-module entity kullanımı, public-contract allowlist'i, provider'ın
  Domain/Application'a sızması ve SharedKernel allowlist'ini ihlal eden örnekle
  red/green kanıtı üretir. Bu gate M8'de genişletilip M10 CI'a bağlanır.

Provider port'ları Application katmanında, adapter ve provider DTO'ları
Infrastructure katmanında kalır. Domain; Cloudflare, iyzico veya Resend
tiplerini görmez. Generic inbox/outbox persistence ve claim mekanikleri
Integration Foundation'a aittir; Payments ve Media daha sonra kendi typed
handler ve domain state geçişlerini sahiplenir. Phase 1 gerçek provider
business entegrasyonu yapmaz ve master credential'ı browser'a taşımaz.

M6 ayrıca Google production sign-in'i doğrulanmış domain + HTTPS config yoksa
startup/feature enable aşamasında fail-closed tutar. Raw-IP/plain-HTTP üzerinde
yalnız private smoke/health açıktır; email/password, Google callback ve gerçek
cookie/session trafiği reddedilir. Bu davranış integration test ile doğrulanır.

M8 BOLA convention'ını yalnız test assembly'sinde oluşturulan iki Creator/iki
Account ve test-only owned-resource fixture ile kanıtlar. Fixture production
API/assembly'ye eklenmez; Phase 1 bu amaçla Invitation endpoint'i, aggregate'ı
veya CRUD davranışı üretmez. İlk gerçek Creator-owned feature aynı negative
test pattern'ini kendi production query'sinde tekrar uygulamak zorundadır.

M9 kabulü yalnız Compose config inspection ile verilemez. İzole Compose stack,
health/readiness ve migration/restore akışı gerçek Docker daemon bulunan local
runner veya CI üzerinde çalıştırılır; container, network, volume ve port
izolasyonu runtime evidence olarak saklanır. Başlangıç workstation'ında daemon
erişimi olmaması bu gate'i düşürmez.

### 6. Açık Product Decision Guard

Aşağıdaki kararların tamamı **unresolved** durumdadır ve Phase 1 tarafından
kapatılmaz:

| ID | Unresolved konu |
| --- | --- |
| PD-01 | Free grant'in yeniden alınabilirliği |
| PD-02 | Organization abonelik bitişinin active window/public içeriğe etkisi |
| PD-03 | `maxPublishDays` düşüşünün başlamış window'a etkisi |
| PD-04 | Boolean entitlement downgrade davranışı |
| PD-05 | Scheduled/Paused kayıtların active quota hesabı |
| PD-06 | Creator/Guest media quota paylaşımı |
| PD-07 | Scheduled cancel/reschedule/hemen yayınlama |
| PD-08 | Trash restore hedef state'i |
| PD-09 | Expired kaydın geleceğe Scheduled edilebilmesi |
| PD-10 | Gift opsiyonel contact alanları ve retention |
| PD-11 | Admin unban yetkisi |
| PD-12 | Renewal/cancel/refund/checkout business akışları |
| PD-13 | Scheduled autosave'in publication semantiği |

Foundation şemaları bu seçeneklerden birini dolaylı olarak kesinleştiremez.
İlgili business milestone başlamadan kullanıcı kararı gerekir.

### 7. Deployment Guard

Phase 1 production deployment içermez. VPS'ye bağlanılmaz, `/opt/davetiye`
oluşturulmaz, Nginx reload edilmez ve canlı trafik açılmaz. Lora container,
image, volume, network, port ve Nginx site'ına hiçbir şekilde dokunulmaz.
Compose/Nginx işleri yalnız local/test foundation ve dokümantasyon olarak
hazırlanır; gerçek VPS işlemi ayrıca açıkça yetkilendirilmelidir.

### 8. Faz Kapanış Kalite Kapısı

M11'de bağımsız Reviewer, Security ve Tester bütün Phase 1 diff ve kanıtlarını
yeniden inceler; Architect ayrıca module/dependency/provider sınırlarını
ADR-0001 ve bu contract'a göre doğrular.

- Unresolved **Critical** veya **High** bulgu varken faz tamamlanamaz.
- Her **Medium** bulgu için owner, risk, disposition ve gerekiyorsa hedef
  milestone/tarih kaydı zorunludur.
- Düzeltmeler owning specialist'e döner; ilgili build/test ile Security veya
  Reviewer kontrolü tekrar edilmeden bulgu kapatılmaz.
- Clean-checkout CI, architecture tests, security tests, gerçek PostgreSQL ve
  gerçek container-runtime kanıtı yeniden doğrulanır.
- PD-01–PD-13 açık durumuyla yeniden raporlanır ve scope sapması kontrol edilir.
- Faz tamamlandıktan sonra yalnız Phase 2 readiness/plan hazırlanabilir;
  kullanıcı açıkça onaylamadan Phase 2 implementation otomatik başlamaz.

### 9. 2026-09-29 Closure Reconciliation

This section supersedes the earlier interrupted-session M7B–M11 status where
it conflicts with the checked repository and command evidence.

#### M7B — Auth route guards

- Added the PII-free, non-cacheable session projection and fail-closed
  Creator/MFA-complete-Super-Admin classification.
- Frontend never renders protected shells before the backend-derived access
  classification permits them. Tests cover permitted Creator/Admin access,
  anonymous denial, authenticated Creator/first-factor denial of Admin, and
  a failed access probe.

#### M8 — Authorization and observability

- A real-PostgreSQL test-only fixture proves foreign Creator internal-ID read
  and mutation queries return no owned resource/affected row.
- Dev email diagnostics now log only a notification kind and code-owned field
  names; recipients, values, URLs and tokens are never emitted. Unhandled
  exception logs retain type/method/correlation only. API and web response
  headers set CSP, nosniff, frame/referrer policy; Production API adds HSTS.
- Confirm-email, reset-password and antiforgery-token routes have separately
  validated per-IP fixed-window policies, with integration coverage.

#### M9 — Compose, runtime and restore evidence

- Local Compose rebuild on 2026-09-29: migrator completed successfully;
  PostgreSQL, API and web were healthy; `/health/live`, `/health/ready` and
  the web root each returned HTTP 200. PostgreSQL has no published host port;
  API and web bind only to loopback.
- Containers use `no-new-privileges`; web/migrator additionally use
  read-only root filesystems, tmpfs and `cap_drop: ALL`. API starts only long
  enough to own its named Data Protection volume and then runs as `app` with
  no effective capabilities.
- Restore rehearsal: a custom-format dump was created from the local named
  Davetiye PostgreSQL service and restored into the isolated, disposable
  `davetiye-restore-rehearsal` PostgreSQL 16 container. SHA-256:
  `ADE6BB5E37B068DEF7B6142D7CA3DA154C9E3EF78D21777839613F58393D5550`.
  `pg_restore --clean --if-exists --no-owner` succeeded; the restored
  database contained the migration-history and accounts tables; the
  disposable container was removed. No VPS or Lora resource was touched.

### 10. UX Foundation Sözleşmesi (Faz 1 M1U Deliverable)

Bu bölüm, önceden ayrı bir dosya olan `docs/PHASE_1_UX_FOUNDATION.md`'nin
tamamıdır (Accepted, Reviewer pass 2026-09-29 — bkz. yukarıdaki M1U notu,
§3 milestone tablosu). M1U milestone'unun kabul edilmiş deliverable'ı
olarak buraya taşınmıştır; içerik değiştirilmemiştir, yalnız başlık
seviyeleri bu belgenin bölüm numaralandırmasına (§10 altında) uyacak
şekilde bir seviye içeri alınmıştır — alt bölüm numaraları (1., 1.1, ...)
orijinal haliyle korunmuştur.

Bu bölüm `docs/PRODUCT.md`, `docs/PHASE_0_PLAN.md`,
`docs/UX_FLOWS.md`, accepted ADR'lar ve yukarıdaki Faz 1 execution kaydını
(§1–§9) uygulanabilir bir UX foundation sözleşmesine dönüştürür. Ürün
kapsamını genişletmez ve kesin görsel tasarım oluşturmaz.

Bu belgede kullanılan etiketler:

- **[Gereksinim]**: Product/baseline/accepted ADR kaynaklı, uygulama ve kabul
  testleri için bağlayıcı davranış.
- **[Foundation kararı]**: Ürün davranışını değiştirmeyen, Faz 1 shell ve
  erişilebilirlik temelinde uygulanacak teknik/etkileşim sözleşmesi.
- **[Öneri]**: Sonraki tasarım çalışmalarında doğrulanacak UX yönü; kabul
  edilmiş ürün davranışı değildir.
- **[Açık karar]**: Kullanıcı kararı olmadan seçeneklerden biri uygulanamaz.

#### 1. Kapsam Koruması

##### 1.1 Bağlayıcı ilkeler

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

##### 1.2 Bu belgede çözülmeyen kararlar

| Konu | Durum | Bu sözleşmenin koruduğu sınır |
| --- | --- | --- |
| Wizard'ın kesin adım sırası | **[Açık karar]** | IA sabit adım adı/sayısı varsaymaz; domain/API sıraya bağımlı olmaz. |
| Kullanıcıya gösterilecek varsayılan timezone | **[Açık karar]** | Tarih/saat özeti timezone'u görünür kılar; varsayılan seçilmez. Teknik instant UTC, timezone IANA ID'dir. |
| PD-07 Scheduled cancel/reschedule/hemen yayınlama | **[Açık karar]** | Scheduled ekranında bu eylemler eklenmez; karar sonrası action slot'una yerleşebilir. |
| PD-08 Trash restore hedef state'i | **[Açık karar]** | Restore eylemi hedef state sözü vermez; doğrulanmış sonuç backend'den gösterilir. |
| PD-09 Expired kaydı geleceğe schedule etme | **[Açık karar]** | Uygun yeni hakla accepted reactivation gösterilebilir; geleceğe schedule seçeneği eklenmez. |
| PD-10 Gift contact alanları ve retention | **[Açık karar]** | Yalnız zorunlu guest adı kesindir; contact control/alan/retention tasarlanmaz. |
| PD-13 Scheduled autosave publication semantiği | **[Açık karar]** | Scheduled için “başlangıçta otomatik yayınlanır” veya “Güncelle gerekir” denmez. |

#### 2. Route, Shell ve Authorization Matrisi

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

#### 3. Mobile-first Creator Shell ve Editor Bilgi Mimarisi

##### 3.1 Creator shell

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

##### 3.2 Creation/editor shell

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

#### 4. Tek Creation Giriş Sözleşmesi

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

#### 5. Autosave, Offline, Conflict ve Active Update Durum Modeli

##### 5.1 Ortak durumlar ve microcopy

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

##### 5.2 Davranış kuralları

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

#### 6. Lifecycle, Confirmation, Empty, Inactive ve Error Matrisi

##### 6.1 Creator lifecycle görünümü

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

##### 6.2 Empty ve unavailable durumları

| Bağlam | Başlık | Açıklama / action sınırı |
| --- | --- | --- |
| Creator invitation listesi boş | `Henüz davetiyeniz yok` | Hızlı creation girişine tek primary action; kapsam dışı örnek feature eklenmez |
| Trash boş | `Çöp kutunuz boş` | Retention veya restore eylemi gösterilmez |
| Sonuç modülü açık, kayıt yok | `Henüz yanıt yok` / feature'a uygun eşdeğer | “Modül kapalı” ile karıştırılmaz |
| Modül kapalı | `Bu bölüm davetiyede kapalı` | Creator'a daha sonra açabileceği anlatılabilir; Guest'e control gösterilmez |
| Feature entitlement nedeniyle yok | `Planınız bu özelliği içermiyor` | “Şimdilik Geçildi” olarak gösterilmez; plan action'ı sonraki commerce UX'e bağlıdır |
| Public inactive | `Bu davetiye şu anda yayında değil.` | `Yayın henüz başlamamış, yayın süresi sona ermiş veya davetiye geçici olarak pasife alınmış olabilir.` İsim, fotoğraf, tarih, OG/media veya neden ayrımı yok |
| Public deleted/unknown | `Sayfa bulunamadı` | Normal 404; invitation varlığı doğrulanmaz |

##### 6.3 Error state ailesi

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

#### 7. Publish Preflight ve Publish Seçimi Low-fi

##### 7.1 Preflight sınıfları

| Sınıf | Örnek kaynak | Davranış |
| --- | --- | --- |
| Required blocker | Template required alanı eksik; renderer uyumsuz; geçersiz tarih/window; entitlement veya hard limit uygun değil | Publish disabled; her blocker özet ve ilgili control linkiyle gösterilir |
| Recommended warning | Önerilen/opsiyonel alan eksik | Publish engellenmez; explicit `Yine de Yayınla` ile devam edilebilir |
| Informational | Public URL, noindex, seçilen başlangıç/bitiş ve timezone özeti | Karar değiştirmez; publish sonucunu anlaşılır kılar |

`required` ve `recommended` metadata template sözleşmesinden gelir; arayüz
bunları kendi içinde hardcode etmez.

##### 7.2 Mobil wireframe

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

#### 8. RSVP, Memories ve Gift Çift Taraflı Akış Sözleşmeleri

Bu bölüm business UI tasarlamaz; Guest ve Creator yüzeylerinin aynı use
case'lerde hangi veriyi görebileceğini ve hangi durumları açıklaması
gerektiğini gösterir.

##### 8.1 RSVP

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

##### 8.2 Memories

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

##### 8.3 Gift Registry

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

#### 9. Responsive ve WCAG 2.2 AA Component Sözleşmesi

##### 9.1 Reflow ve responsive davranış

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

##### 9.2 Ortak component contract'ları

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

##### 9.3 Focus sözleşmesi

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

##### 9.4 Live-region sözleşmesi

| Olay | Kanal | Kural |
| --- | --- | --- |
| Autosave saving/saved | `aria-live="polite"`, coalesced | Her keystroke yok; durum değişimi kısa ve tekil |
| Offline/online dönüş | `polite` | Yalnız değişimde; server'a kaydedilmemiş veri açıkça belirtilir |
| Revision conflict | `role="alert"` veya eşdeğer assertive | Bir kez; ardından focus çözüm yüzeyine |
| Form submit validation | Focus edilen summary + `role="alert"` uygunluğu | Hata sayısı ve linkli özet; inline hatalar ilişkili |
| Upload progress | Görsel progress + seyrek `polite` update | Yüzde değişiminin her adımı duyurulmaz; tamamlandı/başarısız kesin duyurulur |
| Publish/update sonucu | `polite` success, `alert` failure | Working saved ile public updated ayrımı korunur |
| Route loading | Gecikirse `status` | Anlık geçişlerde gereksiz “yükleniyor” gürültüsü yok |

##### 9.5 Görsel ve içerik ölçütleri

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

#### 10. E2E ve Accessibility Acceptance Matrisi

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

#### 11. M1U Completion Checklist ve Self-review

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
