# Faz 1 — Execution Kaydı

Durum: **Tamamlandı ve doğrulandı (17/17 milestone birimi). Phase 2 yalnız taslak olarak hazırdır; uygulanması onaylanmadı.**
Başlangıç tarihi: **2026-09-28**

Bu belge `docs/PHASE_1_PLAN.md` kapsamındaki uygulamanın izlenebilir çalışma
kaydıdır. Ürün veya ADR kararı değildir; kaynak belgeleri değiştirmez.

## 1. Karar Önceliği ve Kapsam Koruması

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

## 2. Başlangıç Repository Checkpoint'i

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

## 3. Milestone ve Bağımlılık Durumu

Owner ve Bulgu sütunları `docs/AI_WORKFLOW.md` §13.3 attribution kuralı
gereği tutulur — bir sorumluluk kaydıdır, ölçülmüş bir efor/iş yükü yüzdesi
değildir. Execution kaydında implementasyon sahibi açıkça isimlendirilmemişse
tahmin yürütülmez, **"Kayıt yok"** yazılır.

| Milestone | Bağımlılık | Durum | Çıkış kapısı | Owner (implementasyon) | Bulgu (Reviewer/Security) |
| --- | --- | --- | --- | --- | --- |
| M0 — Baseline/decision guard | Yok | **Tamamlandı** | ADR index tutarlı, PD-01–PD-13 unresolved | Kayıt yok | — |
| M1 — Repository/dependency contract | M0 | **Tamamlandı** | Topology, sahiplik ve test edilebilir dependency sınırı ADR-0001 ile uyumlu | Kayıt yok | — |
| M1U — UX foundation contract | M0 | **Tamamlandı, Accepted** (Reviewer 2026-09-29) | UX_FLOWS Faz 1 deliverable set'i onaylı ve açık kararları varsaymıyor | UI/UX | Reviewer: 0 blocking; 9/9 deliverable mevcut ve yeterli; 2 Low kayıtlı (açık, düzeltilmedi — microcopy tablolarında [Gereksinim/Öneri] etiketi yok; ban confirmation copy örneği eksik) |
| M2A — .NET backend skeleton | M1 | **Tamamlandı, doğrulandı** (2026-09-28 takeover) | Temiz build, architecture-test başlangıcı ve yalnız izinli project references | Kayıt yok | — |
| M2B — React/TypeScript bootstrap | M1 | **Tamamlandı, doğrulandı** (2026-09-28 takeover) | Feature'sız production build/test shell ve CI skeleton girişi | Kayıt yok | — |
| M3 — PostgreSQL/config/migration foundation | M2A | **Tamamlandı, doğrulandı** (2026-09-28 takeover) | Gerçek PostgreSQL harness ve ayrı migration runner | Kayıt yok | — |
| M4 — API runtime contract | M3 | **Tamamlandı, doğrulandı** (2026-09-28) | `/api/v1`, ProblemDetails, OpenAPI ve health smoke | Backend | Reviewer: 0 Critical/High; Security: 0 Critical/High; 2 Medium + 3 Low kayıtlı (açık, düzeltilmedi) |
| M5A — Identity/Account/typed settings schema | M3 | **Tamamlandı, doğrulandı** (2026-09-28) | Neutral schema ve hard-ceiling/value-type tests | Database (Backend konvansiyonlarıyla koordineli) | Reviewer: 0 Critical/High; Security: 0 Critical/High; 3 Medium kayıtlı (açık, düzeltilmedi) |
| M5B — Inbox/outbox worker primitives | M3 | **Tamamlandı, doğrulandı** (2026-09-28) | PostgreSQL üzerinde idempotent claim/retry tests | Backend | Reviewer: 0 Critical/High; Security: 0 Critical/High; 1 Medium bulundu ve düzeltildi; 2 Low kayıtlı |
| M6 — Security/Admin auth foundation | M4, M5A | **Tamamlandı, doğrulandı** (M6a 2026-09-28, M6b 2026-09-29) | Cookie/CSRF/proxy/rate-limit/DP/MFA, Google fail-closed ve raw-HTTP auth gate tests | M6a: Backend; M6b: Backend | M6a — Reviewer + Security: 0 Critical/High; 1 dormant High kayıtlı (bugün sömürülebilir değil, gelecekteki ban-creation milestone'ı için ön koşul); birkaç Medium bulundu ve düzeltildi; 2 Low kayıtlı. M6b — Reviewer + Security: 0 Critical; 1 dormant High bulundu ve **düzeltildi** (Google sign-in'de ban kontrolü eksikti — M6a'nın kalıcı-sömürülemez High'ından farklı olarak bu hemen düzeltilebilirdi, çünkü kontrol kalıbı zaten mevcuttu); 1 Medium bulundu ve düzeltildi (Google `email_verified` kontrol edilmiyordu); 3 Low bulundu ve düzeltildi (enrollment lockout/rate-limit eksikti; Admin bootstrap mevcut Creator Account'lu kullanıcıya claim verebiliyordu; MfaComplete politikası yalnız doğrudan `AuthorizationService` çağrısıyla kanıtlanmıştı, artık gerçek endpoint+middleware üzerinden kanıtlanıyor); 2 Low kayıtlı (açık, düzeltilmedi — Owner: sonraki dokunuşta Backend): returnUrl heuristiği ADR'nin literal "allowlist" ifadesiyle birebir eşleşmiyor ama fonksiyonel olarak açık redirect üretmiyor; `bypassTwoFactor: true`'nun dayandığı "Google-linked hesap asla Super Admin olamaz" invariant'ı kodda enforce edilmiyor, yalnız yorumla belgelendi |
| M7A — Generated client ve feature'sız route shells | M1U, M2B, M4 | **Tamamlandı, doğrulandı** (2026-09-29) | Responsive/a11y shells ve real-OpenAPI drift check | Frontend (uygulama: Orchestrator; uzman slotu kesinti sonrası erişilebilir değildi) | Tester: 4/4 frontend test dosyası, 8/8 test yeşil; Reviewer/security-focused recheck: 0 Critical/High/Medium, 2 Low açık |
| M7B — Auth route-guard acceptance | M6, M7A | **Tamamlandı, doğrulandı** (2026-09-29) | Public/Creator/Admin guard matrisi integration/E2E doğrulaması | Orchestrator | Frontend 13/13 ve targeted session/rate-limit integration; Reviewer/Security: Critical/High yok; 2 Low takip kaydı |
| M8 — Authorization/architecture/observability verification | M4, M5A, M5B, M6 | **Tamamlandı, doğrulandı** (2026-09-29) | Test-only BOLA fixture, architecture ve redaction tests | Tester + Orchestrator | Real PostgreSQL foreign read/mutate fixture; PII-free DevEmail/exception log ve header testleri; Architecture 66/66, Unit 96/96 |
| M9 — Container/Nginx/backup foundation | M3, M6, M7B | **Tamamlandı, doğrulandı** (2026-09-29) | Gerçek container runtime/CI Compose smoke ve restore rehearsal | Orchestrator | Migrator exit 0; Postgres/API/Web healthy; live/ready/web 200; isolated restore rehearsal başarılı |
| M10 — CI release gate | M7B, M8, M9 | **Tamamlandı, doğrulandı** (2026-09-29) | Clean-checkout pipeline yeşil | Orchestrator | GitHub Actions [run 36534834602](https://github.com/fatihkesik01/Davetiye/actions/runs/36534834602): backend, frontend, Compose health/image scan ve dependency/config scan başarılı |
| M11 — Independent phase closure | M0–M10; M1U ve split M7 dahil | **Tamamlandı, doğrulandı** (2026-09-29) | Reviewer/Security/Tester/Architect onayı ve Phase 2 readiness | Orchestrator | Tester, Security, Reviewer ve Architect kapıları; remote CI ve final repository reconciliation tamam |

### Takeover reconciliation (2026-09-28; historical snapshot, superseded where noted)

A new session picked up this repository after a prior agent's session/token
budget ended. Per `docs/AI_WORKFLOW.md` §1/§11, the claimed state above was
independently re-verified against actual repository content and real
build/test runs before continuing:

- `dotnet build Davetiye.slnx` — succeeded, 0 warnings, 0 errors.
- `Davetiye.ArchitectureTests` — **66/66 passed** (previous session recorded
  62/66; more tests exist now than were last recorded, all green).
- `Davetiye.UnitTests` — **8/8 passed**, including
  `PersistenceConventionTests.PostgreSql_model_applies_foundation_conventions`,
  which the prior handoff recorded as **failing**. The working tree already
  contained a fix for this (naming-convention code and/or test were already
  aligned); it was not re-broken and needed no further change. The previously
  recorded blocker is resolved.
- `Davetiye.IntegrationTests` (`PostgreSqlFixture`, `DatabaseFoundationTests`)
  — **3/3 passed** against a real PostgreSQL Testcontainers instance. The
  prior handoff recorded the Docker daemon as unreachable and this suite as
  not run; this session found Docker Desktop reachable and ran the suite for
  the first time this phase. This satisfies M3's real-PostgreSQL exit gate.
- `npm run build` and `npm test -- --run` in `src/web/` — both succeeded (3
  test files / 6 tests passing), consistent with the prior session's record.
- Inspected `src/backend/Davetiye.Infrastructure/Persistence/ModelBuilderExtensions.cs`:
  applies snake_case table/column/constraint naming, `uuid` for `Guid`,
  `timestamp with time zone` for `DateTimeOffset` (and throws on raw
  `DateTime`), money precision/scale, `Revision` as an `Int64` concurrency
  token, and a `DeletedAt`/`PurgeAfter` soft-delete query filter — matching
  Task 7's convention acceptance criteria.
- The following M4–M7A implementation occurred after this initial snapshot;
  the table above and the dated milestone records are authoritative for their
  current status. This historical paragraph must not be read as a current
  claim that M4/M5A/M5B/M6/M7A are unstarted.
- No `.github/workflows/` and no `deploy/` directory exist. Confirms M9/M10
  have genuinely not started.

**Historical conclusion:** the snapshot verified M0–M3. Subsequent verified
milestones are recorded in the table above; current work is M7B, not M4–M7A.

### M7A — Generated client ve feature'sız route shells (completed 2026-09-29)

Implemented only within `src/web/`: lazy-loaded Public, Creator and Admin
route zones; Turkish feature-less placeholders with distinct shells; global
loading/not-found/error boundaries; skip link, `main` landmark, one visible
`h1`, document-title and route-focus primitives; and no business data,
Invitation/RSVP/Memories/Gift/publish UI, token persistence or client-side
authorization boundary. M7B remains responsible for authenticated guard
acceptance.

The checked-in `openapi/davetiye.v1.json` was produced by `npm run api:sync`
from the live backend `MapOpenApi()` document. `npm run api:verify` downloads
the document named by `DAVETIYE_OPENAPI_URL` and byte-compares it to that
snapshot; `api:generate` creates the versioned cookie-based client and
`api:check` detects generated-source drift. The local API was started with
Development-only test configuration solely to generate/verify this contract;
no deployment or credential was used.

Verification after the Reviewer-found generated-contract Medium fix:

- `npm run api:verify`, `api:check`, lint, typecheck and production build all
  passed.
- Vitest: **4 files / 8 tests passed**, including separate public/Creator/Admin
  shell boundaries and the generated cookie client.
- Independent Tester gate passed. Independent Reviewer recheck reported
  **0 Critical, 0 High, 0 Medium**; its security-focused review found no
  secret storage, unsafe HTML sink, private-data fetch/leak or client-side
  permission assertion.

**Low technical debt (non-blocking):** no explicit test yet covers malformed
or nested route fallbacks and route-focus behavior; browser-level 320 CSS px,
200% zoom, keyboard and automated-a11y evidence remains for the M7B/M8 test
work. These gaps did not justify inventing protected business UI in M7A.
Nothing was reverted or rewritten; only the record was corrected to match
reality.

### M4 — API runtime contract (completed 2026-09-28)

Implemented by Backend: `/api/v1` route prefix convention, sanitized
RFC7807 ProblemDetails (never leaks exception message/stack trace/type in
non-Development, gated on `IHostEnvironment.IsDevelopment()`), correlation
ID (allowlist-validated inbound `X-Correlation-Id`, generated when absent,
propagated to `TraceIdentifier`/logs/response), OpenAPI document generation,
genuinely separate `/health/live` and `/health/ready` (the latter checks
real PostgreSQL connectivity via `DavetiyeDbContext`), Kestrel-level request
body limit with a hard ceiling validated at startup, and fail-closed
forwarded-header/trusted-proxy configuration (nothing trusted unless
explicitly configured; shipped default is loopback-only).

Verification (independently re-run by the orchestrator, not just taken on
the implementer's word):
- `dotnet build Davetiye.slnx` — 0 warnings/errors.
- `Davetiye.ArchitectureTests` — 66/66 passed (no layering regression).
- `Davetiye.UnitTests` — 20/20 passed (8 pre-existing + 12 new
  `ApiHostingOptionsTests`).
- `Davetiye.IntegrationTests` — 12/12 passed (3 pre-existing
  `DatabaseFoundationTests` + 9 new `ApiContractTests`), run against a real
  PostgreSQL via Testcontainers. Proves live/ready genuinely diverge,
  sanitization genuinely differs by environment, correlation ID is
  generated/echoed correctly, and OpenAPI/`/api/v1` respond.
- Independent Reviewer pass: no Critical/High findings; test quality
  confirmed non-tautological; ADR-0001 boundary compliance manually
  spot-checked, not just trusted to the automated gate; scope confirmed
  clean (no M5A/M6 creep).
- Independent Security pass: no Critical/High findings; ProblemDetails
  sanitization, trusted-proxy fail-closed default, and Kestrel body-limit
  enforcement all confirmed correct against `docs/THREAT_MODEL.md`.

**Medium technical debt (owner: Backend/Security, not blocking, track for a
later pass):**
- Trusted-proxy default (`127.0.0.1/32`, `::1/128`) ships in the
  environment-agnostic base `appsettings.json` rather than an
  environment-scoped file; confirm this loopback default is the intended
  production posture (matches the single-host Nginx+Kestrel topology in
  `docs/DEPLOYMENT.md`) rather than dev convenience that was never split out.
- `GlobalExceptionHandler` sets ProblemDetails `Instance` to the raw request
  path. No sensitive route exists yet, but the first route with a
  sensitive path segment (e.g. a token) should revisit this line.

**Low technical debt (owner: Backend/Security, not blocking):**
- `/openapi/v1.json` has no environment guard and is reachable
  unauthenticated in any environment including Production. Not a data leak
  today (schema has no secrets) but is an implicit decision; M6 should
  explicitly decide and record whether prod OpenAPI stays public, moves
  behind an Nginx restriction, or requires auth.
- No end-to-end HTTP test proves Kestrel actually rejects an
  over-the-configured-limit body with 413 (only the options-validation
  ceiling is unit-tested).
- No end-to-end HTTP test proves an untrusted forwarder's forged
  `X-Forwarded-For` is actually ignored (only CIDR/ForwardLimit validation
  is unit-tested). Explicitly deferred to M6, which owns forwarded-header
  trust testing per `docs/PHASE_1_PLAN.md`.

### M5A — Identity/Account/typed Plan/Entitlement/SystemSetting schema (completed 2026-09-28)

Implemented by Database (coordinating entity shape per the Backend
specialist's established conventions): ASP.NET Core Identity integrated
into the single `DavetiyeDbContext` via `IdentityUserContext<ApplicationUser,
Guid>` (users/claims/logins/tokens only — no Roles table, since ADR-0002
treats Creator-vs-Super-Admin as a separate principal/bootstrap concern, not
an Identity-Roles concern); `Account` (Identity & Accounts module, exactly
one per Identity user, enforced by a real DB unique constraint, not just
application code); `BanRecord` (schema/fields only, no enforcement — that's
M6); `EntitlementDefinition`/`EntitlementCatalog` as a code-owned catalog of
supported entitlement keys/value-types/hard-ceilings per ADR-0004; `Plan`,
`PlanEntitlement` (DB-managed commercial values, validated at write-time
against the code catalog's type and ceiling), `AccountPlanGrant` (schema
only, no FK to Account per the modular-monolith cross-module rule, no FK to
any Invitation/PublicationWindow concept since Invitation doesn't exist yet
in Phase 1); `SystemSetting` (typed DB-managed settings). No auth endpoints,
no UserManager/SignInManager wiring, no MFA, no EffectiveEntitlementResolver
business algorithm, and no audit logging were added — all correctly
deferred to M6/M17/later business milestones.

Verification (independently re-run by the orchestrator):
- `dotnet build Davetiye.slnx` — 0 warnings/errors.
- `Davetiye.ArchitectureTests` — 66/66 passed (no layering/cross-module
  regression).
- `Davetiye.UnitTests` — 49/49 passed (20 baseline + 29 new: hard-ceiling
  accept/reject, both directions of value-type mismatch, Account/BanRecord
  guard clauses, SystemSetting typed-value validation).
- `Davetiye.IntegrationTests` — 14/14 passed (12 baseline + 2 new) against
  real PostgreSQL via Testcontainers: the new migration applies cleanly to
  an empty database and is idempotent, and "at most one Account per
  Identity user" is proven rejected by a real Postgres unique-violation
  (23505) using two independent `DbContext` instances — not an EF
  change-tracker artifact.
- Independent Reviewer pass: no Critical/High findings; test quality
  confirmed genuine (not tautological); ADR-0001 module-boundary compliance
  manually verified (zero package references in Domain, no cross-module EF
  navigation, `ProductionArchitectureTests` confirmed to scan real compiled
  assemblies); scope confirmed clean; PD-01–PD-13 confirmed untouched.
- Independent Security pass: no Critical/High findings; the "no DB FK on
  `AccountPlanGrant.AccountId`" and "no Roles table" judgment calls both
  confirmed sound; uniqueness-constraint enforcement confirmed race-safe at
  the Postgres storage layer.

**Medium technical debt (not blocking, track for the milestone that first
needs it):**
- **Owner: Backend/Database, before any milestone actually issues a real
  grant** (Free-grant issuance, payment webhook activation): `AccountPlanGrant.AccountId`
  has no DB-level FK to `accounts` (correct application of the cross-module
  rule), so grant issuance must go through a single Plans & Entitlements
  application service that validates `AccountId` against Identity &
  Accounts' narrow contract before insert — otherwise a typo/stale id could
  create an orphaned grant with nothing to catch it.
- **Owner: Database/Product, before Plan seed data is finalized**:
  `EntitlementCatalog`'s image/video size hard ceilings (`MaxImageSizeMb`
  1,000 MB, `MaxVideoSizeMb` 10,000 MB) are larger than product-realistic
  and are currently the only technical backstop against a bad admin action
  setting a `PlanEntitlement` near them, pending M9's real upload
  enforcement. Recommend tightening (reviewer suggested roughly 25-50 MB
  image / 500 MB-2 GB video) before any Plan is seeded near these values.
- **Owner: Database, before any Account hard-delete path is built**:
  `BanRecordConfiguration` cascades `BanRecord` deletion when `Account` is
  deleted. No hard-delete-of-Account path exists yet (dormant today), but
  ban history is audit-relevant and should probably survive account
  deletion similar to how payment/audit records are not cascade-purged —
  revisit before that path is built.

### M5B — Inbox/outbox worker primitives (completed 2026-09-28)

Implemented by Backend (module: Integration Foundation, per
docs/PHASE_0_BASELINE.md §2 — generic mechanics only, no provider-specific
knowledge): `InboxMessage`/`OutboxMessage` entities (framework-free Domain,
`ProviderName`/`MessageType` as plain string discriminators, opaque payload
storage — no provider enum, no typed payload shape); a real DB-level unique
constraint on `(ProviderName, ProviderEventId)` for inbox idempotency/replay
protection (ADR-0006's intent, applied generically here rather than for
payments specifically); an atomic, concurrency-safe claim primitive using
Postgres `FOR UPDATE SKIP LOCKED` composed into `UPDATE ... FROM ...
RETURNING` (correctness guaranteed by Postgres row-level locking, not
in-process coordination — safe across multiple worker processes); a generic
`BatchMessageWorker<TMessage>` that claims a batch and invokes a
caller-supplied handler delegate, marking success/failure/reschedule
accordingly — no real handler, no real `IHostedService`, and no provider
name (Cloudflare/iyzico/Resend) appears anywhere in production source (the
architecture test's provider-neutrality gate caught and forced a fix of one
XML-doc-comment violation during implementation).

Verification (independently re-run by the orchestrator):
- `dotnet build Davetiye.slnx` — 0 warnings/errors.
- `Davetiye.ArchitectureTests` — 66/66 passed.
- `Davetiye.UnitTests` — 70/70 passed (49 after M5A + 21 new: entity
  invariant guards, `BatchMessageWorker` success/failure/reschedule
  orchestration against a test-only fake claim store).
- `Davetiye.IntegrationTests` — 19/19 passed (14 after M5A + 5 new) against
  real PostgreSQL via Testcontainers: duplicate `(ProviderName,
  ProviderEventId)` genuinely rejected by Postgres (23505, two independent
  `DbContext` instances, mirroring M5A's proof pattern); a real-concurrency
  test (6 independent `DbContext`/connections via `Task.WhenAll`,
  over-requesting a 30-row pool) proves no row is ever double-claimed and
  none is lost; the new migration applies cleanly to an empty database and
  is idempotent.
- Independent Reviewer pass: no Critical/High findings; confirmed the
  hand-retimestamped migration is genuinely additive (only touches the two
  new tables) and the `.Designer.cs` snapshot is genuine EF output, not
  hand-crafted; confirmed module-boundary cleanliness and scope discipline
  by direct grep/inspection, not just the automated gate.
- Independent Security pass: no Critical/High findings; confirmed the claim
  SQL is properly parameterized (no injection) and the idempotency
  constraint is real DB-level enforcement.
- **One real Medium defect found by both passes and fixed before closing
  this milestone**: `BatchMessageWorker.ProcessBatchAsync`'s original bare
  `catch` swallowed `OperationCanceledException` from the batch's own
  cancellation token, misreporting cooperative shutdown as a normal handler
  failure and consuming a retry/backoff slot for a message that was never
  actually attempted. Fixed by adding a specific
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`
  before the general catch, so genuine batch-level cancellation propagates
  instead of being recorded as a failed attempt (a handler's own separate,
  linked per-message timeout is unaffected and still correctly counts as a
  failure). Re-verified after the fix: 66/66, 70/70, 19/19 all still green.

**Low technical debt (not blocking):**
- **Owner: Backend/Reviewer, style only**: `InboxMessageRepository`/
  `OutboxMessageRepository` are `public` rather than `internal` so
  integration tests can construct them directly — Reviewer confirmed this
  actually matches existing precedent (`DavetiyeDbContext`,
  `DatabaseMigrationRunner` are also `public` and already directly
  constructed by tests), so this is not a deviation worth changing.
- **Owner: whichever milestone first writes a real payload (Payments/
  Media/Notifications)**: `Payload` columns are unbounded `text` with no
  redaction hook. Reasonable for this foundation-only milestone (the module
  doesn't decode payloads), but the first real handler must decide what
  subset of a raw provider payload is safe to persist and set an explicit
  max length — nothing at this layer will stop it from doing the wrong
  thing.

### M6a — Core email/password auth + cookie/CSRF/CORS/rate-limit/Data
Protection baseline (completed 2026-09-28)

M6 was split into two verifiable slices; M6a covers everything except
Google OAuth, Admin bootstrap and TOTP/MFA (those are M6b). This milestone
was interrupted once mid-implementation by a session/token limit, leaving
the repository in a non-building state (3 compile errors, `Program.cs`
never wired up, no endpoints, no tests). Per `docs/AI_WORKFLOW.md` §1, the
orchestrator independently verified the actual state before continuing
(did not trust the interrupted agent's progress marker), found the
partial work well-designed but genuinely incomplete, and had a second
agent session complete it rather than discard it.

Implemented: `AddIdentityCore<ApplicationUser>()` DI wiring (no Roles
table, per M5A's established design); `__Host-`-prefixed production
cookie (validated combination: Secure/HttpOnly/SameSite=Lax/Path=`/`/no
Domain); `SecurityStampValidator` wired to re-validate every request so
password-reset genuinely revokes already-issued cookies (proven by a real
test, not just config inspection); antiforgery; exact-origin CORS (no
wildcard, validated); per-IP rate limiting on register/login/password-
reset-request; persistent, gitignored Data Protection key ring; a minimal
`IEmailSender` port with `DevEmailSender` (Development-only) and
`UnavailableEmailSender` (fails closed everywhere else, mirroring
`FakePaymentGateway`'s required production posture per
docs/THREAT_MODEL.md §9); full auth endpoints (register — Individual
accounts only, confirm-email, login, logout, request-password-reset,
reset-password) with ban checking at login and email-confirmation
required before login; a raw-HTTP fail-closed proof (auth traffic
rejected without `X-Forwarded-Proto: https` in Production, while
`/health/live`/`/health/ready` remain reachable).

Verification (independently re-run by the orchestrator after each pass):
- `dotnet build Davetiye.slnx` — 0 warnings/errors.
- `Davetiye.ArchitectureTests` — 66/66 passed throughout (no boundary
  regression from the new Identity/cookie/CORS/rate-limiter wiring).
- `Davetiye.UnitTests` — final count **87/87** (70 after M5B + 17 new:
  15 options-validator boundary tests + 2 for token-redaction in
  `DevEmailSender`).
- `Davetiye.IntegrationTests` — final count **30/30** against real
  PostgreSQL via Testcontainers (19 after M5B + 11 new: the original 8
  auth-flow tests plus 3 added during the fix pass — confirmed-email
  login gate, Production email-adapter fail-closed, cross-origin
  form-encoded login-CSRF regression guard).
- Independent Reviewer pass and independent Security pass, both with
  extra scrutiny on the changes to pre-existing code (an architecture-test
  `SharedKernel` exemption, a pre-existing integration test's Production
  config, a doc-comment provider-name fix) — all confirmed legitimate and
  narrow, no weakening found.
- **Real findings from those passes were fixed and re-verified, not just
  logged**: login now requires a confirmed email (previously cosmetic —
  `RequireConfirmedAccount` was never set); the Data Protection key
  directory is now actually gitignored (previously contradicted its own
  doc comment); all 7 new options-validator types got direct boundary
  unit tests (matching the M4/M5A precedent this milestone had skipped);
  `DevEmailSender` no longer logs raw single-use tokens (redacted) and no
  longer runs outside Development (fails closed via
  `UnavailableEmailSender` elsewhere); the login/register/reset-password
  CSRF-exemption reasoning was corrected (JSON-body-binding + strict CORS,
  not "credentials in the body") and locked in with a regression test.

**High finding — dormant today, but a hard prerequisite for a future
milestone (owner: whichever milestone first adds ban creation; record
this now, do not let it become generic "technical debt"):** creating a
`BanRecord` does not currently revoke an already-issued session — nothing
touches the Identity user's security stamp when a ban is created, and
`SecurityStampValidator` only compares that stamp. This is **not
exploitable today** because no ban-creation endpoint exists anywhere in
the codebase yet (M5A built `BanRecord` schema only). **The milestone that
first adds a ban-creation endpoint (likely part of Super Admin/
Administration) MUST call `UserManager.UpdateSecurityStampAsync` (or
equivalent) on the banned user in the same transaction/operation as
creating the `BanRecord`, mirroring the pattern `AuthAccountService.
ResetPasswordAsync` already uses** — otherwise banning becomes a no-op
against any already-active session the instant that endpoint ships, which
would be a live High/Critical gap at that point per
docs/THREAT_MODEL.md T11.

**Medium technical debt (not blocking, track with owner):**
- **Owner: whichever milestone wires a real production email adapter**:
  `AuthAccountService.RegisterAsync` commits the Identity user + Account
  transaction *before* calling `emailSender.SendAsync(...)`. In
  Production this is now actually reachable (previously masked because
  `DevEmailSender` silently no-op-succeeded everywhere) — a Production
  register attempt today creates a real account but returns 500 via
  `UnavailableEmailSender`. Not fixed in this pass (out of the fix
  task's scope; Phase 1 doesn't deploy to production), but the milestone
  that adds a real adapter must either reorder this (send/queue before
  commit, or compensate on send failure) or explicitly accept partial-
  failure semantics.
- **Owner: Security/Backend, before Phase 1 closes**: no rate limiting on
  `/auth/confirm-email`, `/auth/reset-password`, or
  `GET /api/v1/antiforgery/token` (only register/login/password-reset-
  request carry `RequireRateLimiting`, inconsistent with
  docs/THREAT_MODEL.md T14/T16's stated intent for this whole endpoint
  class). Token entropy makes this low-exploitability, but worth closing.
- **Owner: whoever owns M4's carried-over item**: forwarded-header trust
  for the rate-limiter's per-IP partition key is correct by code
  inspection (`UseForwardedHeaders` runs before `UseRateLimiter`) but
  still has no end-to-end test proving a forged `X-Forwarded-For` from an
  untrusted source is ignored — this was already logged as M4 technical
  debt and remains unmet.

**Low technical debt (not blocking):**
- Login has a timing side-channel (real password-hash comparison only
  runs when the email exists) — minor account-enumeration signal, not
  covered by an explicit threat-model requirement for login specifically.
- `request-password-reset`'s response latency differs between existing
  and non-existing accounts (email dispatch only awaited on the existing-
  user path) — proven response-body-identical, but timing differs; will
  matter more once `IEmailSender` is a real network call.

### M6b — Google OAuth + Admin bootstrap + TOTP/MFA (completed 2026-09-29)

Implemented by Backend, building directly on M6a's cookie/Identity
foundation without redesigning it (one narrow, necessary, well-justified
exception: `SecurityServiceCollectionExtensions`'s `OnValidatePrincipal`
now preserves a per-session `amr=mfa` claim across
`SecurityStampValidator`'s per-request regeneration — proven a pure no-op
for every non-2FA session by both independent review passes). Delivered:
Google authorization-code sign-in (PKCE, minimal `openid email profile`
scope, external identity keyed on Google `sub`, no silent account merge
on a matching email, fail-closed at host startup in Production without a
verified HTTPS callback — proven by a real test that asserts the host
fails to start); a controlled one-time Admin bootstrap console tool
(`tools/Davetiye.AdminBootstrap/`, mirroring `Davetiye.DatabaseMigrator`'s
shape) that grants a claim-based Super Admin marker (no Identity Roles
table, per M5A's established design) and structurally cannot create a
Domain `Account`; TOTP MFA + single-use recovery codes using ASP.NET Core
Identity's built-in provider (no custom crypto) with real lockout
protection on the two-factor login-completion step; and a reusable
`"MfaComplete"` authorization-policy contract (claim-based: Super Admin +
per-session 2FA-completion marker) for future Admin endpoints to apply.
No real Admin business module (plan management, ban creation, audit log)
was built — correctly out of scope.

Verification (independently re-run by the orchestrator after each pass):
- `dotnet build Davetiye.slnx` — 0 warnings/errors throughout.
- `Davetiye.ArchitectureTests` — 66/66 passed throughout (no regression
  from the new Google/MFA/Admin-bootstrap wiring or the new
  `Davetiye.AdminBootstrap` tool project).
- `Davetiye.UnitTests` — final count **94/94** (87 after M6a + 7 new
  options-validator boundary tests for `GoogleAuthOptions`/`MfaOptions`).
- `Davetiye.IntegrationTests` — final count **39/39** against real
  PostgreSQL via Testcontainers (30 after M6a + 6 from initial
  implementation + 3 more from the fix pass): Google fail-closed startup,
  first-time Google sign-in creates a real `Account`, email-collision
  correctly does NOT merge (proven by asserting zero `UserLogins` rows),
  Admin bootstrap idempotency and its Account-guard refusal, a full
  RFC 6238 TOTP round-trip (enroll → verify → two-factor login-complete)
  using a real computed code — not a bypass, single-use recovery-code
  redemption, `MfaComplete` policy rejection for both a plain Creator and
  an unenrolled Super Admin (now proven through a real mapped endpoint +
  `UseAuthorization()` middleware, not just a direct
  `IAuthorizationService` call), Google sign-in rejection for an
  unverified email, and Google sign-in rejection for a banned account via
  the existing-linked-identity fast path.
- Independent Reviewer and Security passes, both with extra scrutiny on
  the `OnValidatePrincipal` change and the `AuthAccountService.LoginAsync`
  diff — both confirmed correct and provably a no-op/unaffected for every
  M6a-era (non-2FA) session, not just trusted to the aggregate test count.
- **Real findings from those passes were fixed and re-verified, not just
  logged**: Google `email_verified` is now checked and enforced
  fail-closed (was previously unchecked — the same email-squatting risk
  M6a's `RequireConfirmedAccount` closed for the password path, reopened
  via the Google path); Google's existing-linked-identity sign-in now
  checks `BanRecords` before completing sign-in (previously had no ban
  check at all, unlike the password-login path); TOTP verification during
  enrollment now has lockout accounting and a rate limit (previously
  unlimited); Admin bootstrap now refuses to grant the Super Admin claim
  to a user who already has a Creator `Account` (previously could
  silently violate the documented "Super Admin has no Account"
  invariant); the `MfaComplete` policy's proof was strengthened to route
  through a real mapped endpoint and ASP.NET Core's actual
  `UseAuthorization()` middleware instead of a direct
  `IAuthorizationService` call.

**High finding — dormant, fixed immediately (unlike M6a's structurally-
blocked High, this one had no such blocker):** Google's
existing-linked-identity sign-in fast path had no ban check at all,
unlike the email/password login path's established M6a convention. Not
exploitable today (no ban-creation endpoint exists anywhere yet — same
root cause as M6a's dormant High), but unlike that one, the fix here
didn't require a not-yet-built ban-creation flow — the `BanRecords` table
and query pattern already existed, so it was fixed and tested in this
same pass rather than merely recorded as a future prerequisite.

**Medium finding — fixed:** Google sign-in never checked the `email_verified`
claim before treating an address as confirmed, reopening the
email-squatting risk M6a's `RequireConfirmedAccount` was added to close
for the password path. Now mapped, checked, and enforced fail-closed
(missing/false claim rejects sign-in and creates no account).

**Low technical debt (not blocking, owner: next milestone/touch that
relies on it):**
- `GoogleSignInService`'s `ResolveSafeReturnUrl` satisfies the actual
  security property (no open redirect is possible — the accepted value
  is only ever appended to the trusted, fixed `PublicWebOptions.BaseUrl`,
  never a different origin) via a relative-path-only heuristic, not the
  literal enumerated "local allowlist" docs/adr/0002 and
  docs/THREAT_MODEL.md §6 describe. Functionally safe today; revisit if
  this code is touched again to either implement the literal route
  allowlist or explicitly document/reject control characters (currently
  relies implicitly on the framework's own header-value validation).
- `GoogleSignInService.CompleteSignInAsync` passes `bypassTwoFactor: true`
  on the sound but code-unenforced assumption that a Google-linked
  identity can never belong to a Super Admin (true today only because no
  Google-account-linking-to-an-existing-user flow exists — collision
  always returns 409 without linking). Whichever milestone first adds
  account-linking must re-verify this invariant before this bypass
  remains safe.

M5A ve M5B paralel tasarlanabilir; ancak `DbContext`, EF configuration,
model snapshot, DI registration ve migration dosyalarına yapılan değişiklikler
Database specialist'in merge sırasıyla tamamen serialize edilir. Aynı dosyaya
eşzamanlı yazım yapılmaz. CI skeleton M2A/M2B ile başlar; zorunlu clean-checkout
gate ve scan'ler M10'da tamamlanır.

### M1U UX foundation contract (Accepted 2026-09-29)

Independent Reviewer pass (2026-09-29) found all 9 required deliverables
below present and substantively covered (not superficial), no scope
violation (no visual design specified, no Phase-1-excluded business
feature implemented), no assumption of any open product decision (PD-07/
08/09/13 explicitly avoided per lifecycle-row, PD-10 confined to one
tagged open-decision point, PD-11 correctly reflected as absent), and no
contradiction with `docs/PRODUCT.md`/`docs/UX_FLOWS.md`/
`docs/PHASE_0_BASELINE.md` anywhere cross-checked (route list,
authorization language, lifecycle table, RSVP/Memories/Gift capability
model, publish preflight rules all verified consistent). Two Low,
non-blocking notes recorded: the microcopy/state tables don't carry a
per-row `[Gereksinim]/[Foundation kararı]/[Öneri]/[Açık karar]` tag the
way the document's prose sections do (minor ambiguity about whether exact
microcopy strings are locked contract text); no ban-confirmation copy
example exists analogous to the delete-confirmation one (low priority —
Admin shell is explicitly placeholder-only in Phase 1 and no
ban-creation endpoint exists anywhere yet). Document status updated from
"review adayı" to "Accepted" in `docs/PHASE_1_UX_FOUNDATION.md`.

UI/UX bu milestone'da, shell kodundan önce, `docs/UX_FLOWS.md` Faz 1 listesinin
tamamını feature geliştirmeden üretir ve Reviewer'a sunar:

1. Public/Creator/Admin route ve authorization matrisi.
2. Mobile-first Creator shell/editor low-fi bilgi mimarisi.
3. Public katalogdan veya panelden başlayan creation girişlerini tek wizard
   use case'inde birleştiren akış contract'ı; kesin wizard adım sırası seçilmez.
4. Autosave, offline recovery, revision conflict ve Active explicit update
   durumlarının microcopy/state modeli.
5. Lifecycle action, confirmation, empty, inactive ve error state matrisi;
   PD-07/08/09/13 seçeneklerinden biri varsayılmaz.
6. Required blocker ile recommended warning'i ayıran publish preflight ve
   immediate/scheduled publish low-fi wireframe'i.
7. RSVP, Memories ve Gift için Guest/Creator çift taraflı akış şemaları;
   business UI veya PD-10 contact kararı uygulanmaz.
8. Responsive layout ve WCAG 2.2 AA component/focus/live-region contract'ı.
9. 320 CSS px, 200% zoom, keyboard, route/modal focus, async announcement ve
   temel Public/Creator/Admin sınırlarını kapsayan E2E/a11y acceptance matrisi.

M7A bu accepted contract'ı shell düzeyinde uygular. M7B ise ancak M6 güvenlik
temeli tamamlandıktan sonra auth route guard'larını kabul edilmiş route ve
authorization matrisiyle doğrular.

## 4. Repository Topology ve Dosya Sahipliği

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

### Migration mekanizması

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

## 5. Dependency ve Runtime Sınırı

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

### Test edilebilir modular-monolith konvansiyonu

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

## 6. Açık Product Decision Guard

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

## 7. Deployment Guard

Phase 1 production deployment içermez. VPS'ye bağlanılmaz, `/opt/davetiye`
oluşturulmaz, Nginx reload edilmez ve canlı trafik açılmaz. Lora container,
image, volume, network, port ve Nginx site'ına hiçbir şekilde dokunulmaz.
Compose/Nginx işleri yalnız local/test foundation ve dokümantasyon olarak
hazırlanır; gerçek VPS işlemi ayrıca açıkça yetkilendirilmelidir.

## 8. Faz Kapanış Kalite Kapısı

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

## 9. 2026-09-29 Closure Reconciliation

This section supersedes the earlier interrupted-session M7B–M11 status where
it conflicts with the checked repository and command evidence.

### M7B — Auth route guards

- Added the PII-free, non-cacheable session projection and fail-closed
  Creator/MFA-complete-Super-Admin classification.
- Frontend never renders protected shells before the backend-derived access
  classification permits them. Tests cover permitted Creator/Admin access,
  anonymous denial, authenticated Creator/first-factor denial of Admin, and
  a failed access probe.

### M8 — Authorization and observability

- A real-PostgreSQL test-only fixture proves foreign Creator internal-ID read
  and mutation queries return no owned resource/affected row.
- Dev email diagnostics now log only a notification kind and code-owned field
  names; recipients, values, URLs and tokens are never emitted. Unhandled
  exception logs retain type/method/correlation only. API and web response
  headers set CSP, nosniff, frame/referrer policy; Production API adds HSTS.
- Confirm-email, reset-password and antiforgery-token routes have separately
  validated per-IP fixed-window policies, with integration coverage.

### M9 — Compose, runtime and restore evidence

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

### M10/M11 remaining remote gate

The GitHub Actions workflow is committed and the local equivalents,
dependency audits, image build and Compose smoke were verified. Its first
remote run is the remaining M10 prerequisite; M11 must not be marked closed
until that run succeeds. This is intentionally not inferred from local work.
