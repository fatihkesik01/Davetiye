# Faz 9 — Super Admin & Platform Governance Planı

Durum: **ONAYLANDI — Phase 9 uygulaması 2026-10-06'da Fatih tarafından açıkça onaylandı; 7/7 milestone ile tamamlandı**
Bağımlılık: **Faz 3'ün uygulanması, tamamlanması ve bağımsız doğrulanması (gerçek commerce görünümü için ayrıca Faz 8)**

Bu plan başlangıçta 2026-09-29'da erken taslak olarak hazırlanmıştı. Phase 0–8
tamamlandıktan sonra Fatih 2026-10-06'da Phase 9 uygulamasına açıkça onay
verdi. Faz içindeki açık ürün kararları yalnız bağlı oldukları milestone'ları
bloklar; karar verilmemiş davranış varsayılmaz.

Kaynak: Bu taslak `docs/ROADMAP.md` §14 (Phase 9)'daki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

MFA-complete Super Admin için aggregate dashboard, kullanıcı/ban, plan-
entitlement-setting, ödeme, storage/health ve minimize audit yüzeylerini
kurmak. Bu faz var, çünkü platform production'da yalnız feature sunmakla
değil, güvenli biçimde yönetilebilmekle tamamlanır: Super Admin ayrı
panelde platformu yönetir; normal Creator/Guest private content'ine genel
bypass kazanmaz.

## Kapsam

Aggregate read model'ler, accepted ban/unban, plan/settings editörü,
ödeme/storage/system health, audit, kontrollü bootstrap/recovery runbook.

## Kapsam Dışı

Organization team'leri; özel RSVP/Memory/Gift içerik tarama; kullanıcı
tarafından yazılan template'ler; genel impersonation.

## Bağımlılıklar

Faz 3'ün tamamlanması; gerçek commerce görünümü için Faz 8; Faz 1'in MFA
foundation'ı.

## Bu Fazdaki Ürün Kararları

Fatih 2026-10-06'da lost-factor recovery için şu kararı verdi: yalnız
platform sahibi kurtarmayı başlatabilir; hesap sahipliği dış kanaldan
doğrulanır, MFA sunucu tarafında yeniden kurulur ve işlem audit'e yazılır.
Kurtarma public/Admin HTTP endpoint'i değildir; kontrollü sunucu operasyonu
ve runbook ile uygulanır.

PD-11, Fatih tarafından 2026-10-06'da kabul edildi: MFA-complete Super
Admin unban yapabilir; işlem audit'e yazılır ve erişim geri gelir. Bu karar
P9-M3'ü açar. Fatih ayrıca Admin'in banlı hesap listesinde Creator e-postasını
görmesine ve e-posta öneki ile aramasına 2026-10-06'da onay verdi.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Aggregate Admin read model'leri | Faz 3 (uygulanabilirse Faz 8) | Backend, Database, Frontend | Kullanıcı/hesap tipi, invitation state, plan, ödeme, storage, health aggregate-only projection'lar; private guest content veya raw provider payload yok. |
| 2 | Ban lifecycle | 1 | Backend, Security, Database | Ban reason/note/audit, login deny, session/security-stamp revoke, public delivery overlay; Faz 1'in dormant High bulgusu (ban session revocation) kapatılır; mevcut session hemen geçersiz olur; public/media request-time shutdown testli. |
| 3 | Unban/identity administration | 2 + accepted PD-11 | Backend, Frontend, Security | MFA-complete unban command and safe account summaries; command authorization, audit and recovery tests pass. |
| 4 | Plan/settings/template metadata | 1 | Backend, Database, Frontend, Security | Typed editör, hard ceiling, confirmation, change audit; invalid/maliyetli değer backend'de reddedilir; limit düşüşü veri silmez; concurrency testli. |
| 5 | Ödeme/storage/health/audit | 1 (Faz 8 varsa) | Backend, Frontend, Security | MFA-complete paginated operational payment and audit views, health status labels, precise DB-verified media usage summary; no secret, provider payload, PII, or private response data; audit remains append-only and is not automatically deleted before the Phase 10/11 legal retention decision. |
| 6 | Admin UX ve recovery | 1–5 | Frontend, UI/UX, Backend, Security | Responsive ayrı shell, MFA enrollment/login/recovery-code, kontrollü lost-factor runbook; gerçek middleware `MfaComplete`; public bypass yok; 320 px/200%/keyboard testli. |
| 7 | Governance kapanışı | 1–6 | Tester, Security, Reviewer, Architect | Cross-module permission/audit review; Admin'in genel data bypass'ı olmadığı, ban/session/audit/hard-ceiling testlerinin yeşil olduğu kanıtlanır; Critical/High yok. |

### Milestone durumu ve bağımsız doğrulama

| Milestone | Durum | Implementer | Tester/Reviewer | Security |
| --- | --- | --- | --- | --- |
| M1 | Complete: aggregate API + responsive dashboard; backend PG 3/3; web 208/208, build/lint/API contract green | Backend + Orchestrator (web) | ACCEPT (0 C/H/M/L) | ACCEPT (0 C/H/M/L) |
| M2 | Complete: ban command, session/login/public/media enforcement, audit and generated API contract verified | Database + Backend | ACCEPT (0 C/H/M/L) | ACCEPT (0 C/H/M/L; 1 High found and fixed before verification) |
| M3 | Complete: unban and bounded banned-account listing with Creator email-prefix search | Backend + Frontend | ACCEPT (0 open; PostgreSQL 15/15, web/API contract verified) | ACCEPT (0 open; query-string PII finding closed by POST body) |
| M4 | Complete: plan/template governance, immutable price/billing snapshots, and limited typed global retention settings editor | Backend + Database + Frontend | ACCEPT (full suite: PostgreSQL 501/501, Unit 482/482, Architecture 79/79; web 251/251; API contract/build/lint/typecheck green) | ACCEPT (0 open; two-key allowlist, MFA/HTTPS/CSRF/rate-limit/audit/revision checks, five-minute intermediate auth cookies, Google-link regression fixed) |
| M5 | Complete: paginated payment/audit views, bounded projections, responsive UI, no-store/HTTPS/rate-limit controls | Backend + Database + Frontend | ACCEPT (0 open; pagination finding fixed and re-tested) | ACCEPT (0 open; HTTPS finding fixed and re-tested) |
| M6 | Complete: MFA setup/login UX, enabled-factor rebind guard, platform-owner-only trusted recovery CLI/runbook, atomic reset/audit and immediate session revocation | Backend + Frontend | ACCEPT (MFA/Google PostgreSQL 25/25; web auth/setup 23/23; browser flow 3/3 across desktop, 320px and 200% zoom; route loop fixed before close) | ACCEPT (0 open findings; owner/host trust boundary recorded) |
| M7 | Complete: whole-phase authorization, audit, architecture, API contract, hard-ceiling, session and privacy closure | Orchestrator | ACCEPT (full solution: Architecture 79/79, Unit 482/482, PostgreSQL Integration 501/501; web 251/251; API contract/build/lint/typecheck green) | ACCEPT (0 open findings; intermediate-cookie lifetime/link flow and typed settings protections verified) |

M2 schema migration `20261006101308_P9M2AdminBanAuditSchema` is generated
but not applied. It adds the optional bounded BanRecord note and append-only
minimized AdminAuditRecord, prevents account deletion from cascading over Ban
history, and enforces one active ban per account. The MFA-complete ban command
is audited atomically with the ban and target security-stamp rotation. Cookie
validation checks active bans on every authenticated request, independently
of the configurable security-stamp interval; the PostgreSQL regression uses a
900-second interval. Login and public/media requests also enforce the ban.

M2 verification: solution build 0 warnings/errors; ArchitectureTests 79/79;
UnitTests 472/472; focused PostgreSQL AdminBan integration 4/4; PublicInvitation
and PublicMedia integration 60/60; web Vitest 209/209, production build, lint,
and `api:check` passed. Tester/Reviewer and Security ACCEPT with 0 open
findings. Security found one High interval-related session-revocation issue;
the per-request ban check fixed it before final verification. Existing media
capabilities already issued before a ban remain usable only for their bounded
short TTL, as allowed by the threat model. Migration is not applied to a
non-test database; preflight historical duplicate active bans before apply.

### P9-M3 implementation and verification

PD-11 was accepted by Fatih on 2026-10-06: MFA-complete Super Admin may unban;
the action is audited and access returns. `POST
/api/v1/admin/accounts/{accountId}/unban` uses the same MFA-complete,
rate-limited, HTTPS-in-production and antiforgery boundary as ban. It preserves
the BanRecord with `RevokedAt`, rotates the target security stamp, and appends
minimized `AccountUnbanned` audit in one transaction. Unknown account returns
404; account without an active ban returns 409. The OpenAPI contract and
generated web API client include the no-body 204 command.

Security review found that a copied pre-ban cookie never presented during the
ban could become usable after unban while a nonzero security-stamp validation
interval remained. Cookie validation now forces stamp comparison for any
identity with ban history, while preserving the configured interval for other
accounts. Regression coverage captures a pre-ban cookie, does not send it
during the ban, replays it after unban at a 900-second interval, verifies it is
rejected, and verifies fresh login succeeds. Focused PostgreSQL AdminBan tests
pass 6/6; solution build passes with 0 warnings/errors; API contract/client
tests pass 29/29 and TypeScript typecheck passes. Tester/Reviewer and Security
independently ACCEPT with 0 open findings after the High fix. Fatih approved
showing Creator email and supporting email-prefix search in the banned-account
list. The account summary projection is limited to `accountId`, `displayName`,
`accountType`, nullable `email`, `createdAtUtc`, `bannedAtUtc`, and `reason`; it
omits internal notes, identity-provider details, invitation IDs, and private
guest content. The searchable listing is
`POST /api/v1/admin/accounts/banned/search`; paging and optional `emailPrefix`
are sent in a bounded JSON body, avoiding email PII in the URL. It requires
MFA-complete authorization, the read limiter, HTTPS in production, antiforgery,
and no-store responses. It uses stable ordering, 1-based paging (default 50,
maximum 100), and caps the email prefix at 256 characters.

P9-M3 verification: focused PostgreSQL ban/unban/account-list integration
tests pass 15/15 (AdminBan 6/6; account listing 9/9), including
authorization, antiforgery, bounded search, paging, projection allowlist, and
OpenAPI request shape. Solution build passes with 0 warnings/errors. Web
Vitest passes 223/223; lint, typecheck, production build, and `api:check` pass.
The UI handles loading/error/empty states, pagination, email-prefix search,
unban confirmation, and refresh after success. Tester/Reviewer ACCEPTed with
0 open findings after running the focused PostgreSQL tests (15/15), focused
web tests (38/38), `api:check`, and typecheck. Security ACCEPTed with 0 open
findings after independently verifying that the prior query-string PII issue
is closed: the OpenAPI route has no query parameters, application diagnostics
do not log request bodies, and the Nginx configuration does not log bodies.

### P9-M4 approved scope and implementation status

On 2026-10-06, after the architecture review clarified the milestone's
“settings” scope, Fatih explicitly chose a limited typed global-setting editor
as part of M4. The editor is restricted to the two existing integer retention
settings (`deletedInvitationRetentionDays` and
`abandonedMemoryRetentionDays`), each bounded to 0–365, with revision checks
and audit. No arbitrary key/value editor or new setting keys are in scope.
Administration owns the setting entity, descriptors, readers, and seeders;
Invitation and Memories modules consume narrow integer-only read contracts.
The `/api/v1/admin/settings` list and `/api/v1/admin/settings/{key}` update
routes are MFA-complete, rate limited, HTTPS-in-production, no-store, and
antiforgery protected for writes. Concurrent updates use revision compare-and-
set, and mutations append minimized audit rows transactionally. Draft UI keeps
the unsaved value on conflict and requires confirmation before writes. The
generated API client and OpenAPI checks cover both operations while preserving
the existing session, MFA, subscription-null, ban and account-search contracts.
M4 verification: focused settings/OpenAPI PostgreSQL integration 4/4;
focused settings/payment unit coverage 28/28; full ArchitectureTests 79/79,
UnitTests 482/482, IntegrationTests 501/501; web tests 251/251; solution build,
`api:check`, lint, typecheck and production build all pass. Independent
Tester/Reviewer, Security and Architect reviews found no open findings.

On 2026-10-06 Fatih approved an Admin editor for plan display name, optional
description, amount, billing type (`Free`/`OneTime`/`Monthly`), and the existing
typed entitlement limits; template name, optional description, and visibility
(`IsActive`). Plan key/currency and entitlement keys/types/hard ceilings remain
code-owned. Template key/category/premium flag/renderer version/preview/module
and required/recommended field metadata remain outside this editor. The
description columns are nullable, bounded to 2,000 characters, and the additive
migration leaves existing descriptions NULL. Admin writes will require typed
validation, hard ceilings, explicit confirmation, expected revision, and a
minimized audit record.

Fatih also decided that hiding a template removes it from discovery and new
selection while preserving editing and publication for existing pinned Drafts;
published invitations keep resolving their pinned renderer. This behavior is
implemented and its focused publication/catalog integration tests pass 36/36.
Plan billing type is explicitly editable. Fatih decided a billing-type change
applies only to new acquisitions; existing grants and subscriptions preserve
their prior behavior. `AccountPlanGrant.BillingKindAtGrant` is immutable and
backfilled from grant source; payment attempts snapshot the checkout-time type
so a later catalog edit cannot invalidate settlement. The plan API, plan editor,
and template slice are implemented and independently accepted. Fatih also decided
plan price changes apply only to new acquisitions; existing Organization
subscriptions retain their activation price. The plan update endpoint now
accepts a bounded `numeric(19,4)` price. Organization subscriptions store the
verified activation price; the additive migration backfills existing rows from
the plan price because Admin price editing did not previously exist. New
subscriptions snapshot the checkout-time price, and access summaries use that
snapshot after subsequent plan changes. New individual checkouts already snapshot
the current plan price. Currency and plan key remain code-owned. No provider-side
renewal update is part of this decision. Reviewer and Security accepted M4 with
no open findings after the plan-save race fix.

### P9-M5 scope and accepted retention behavior

M5 reuses the aggregate Admin overview as its dashboard. Operational payment
and audit reads are MFA-complete, read-only, no-store, rate limited, stably
ordered, and server-paginated with a hard page-size cap. Payment projection
excludes account/invitation IDs, checkout URLs, provider identifiers,
webhook/idempotency data, and payloads. Audit projection contains only actor
and subject IDs, UTC timestamp, and bounded event type. Health output remains
status labels only. Storage continues to report database-verified media bytes;
it does not claim Cloudflare billed/account usage and does not expose asset or
provider references. Fatih accepted on 2026-10-06 that Phase 9 must not
automatically delete audit rows while the exact legal/operational retention
period remains open for Phase 10/11.

M5 implementation details: payment and audit endpoints use 1-based paging,
default page size 50 and maximum 100; invalid or overflowing query values
return 400. Payment rows sort by `UpdatedAt DESC, Id DESC`; audit rows sort
by occurrence time and ID descending. A payment page is a live offset view,
so updates between requests can shift later pages. Admin API responses,
including malformed-query and rejected requests, carry no-store headers. The
Admin overview is HTTPS-protected in production. Migration
`20261006104138_P9M5PaymentAttemptUpdatedAtPagingIndex` adds only the payment
paging index and has not been applied outside test databases. No cleanup job
deletes audit rows.

M5 verification: PostgreSQL operational-read and overview integration tests
10/10; focused persistence tests 15/15; UnitTests 473/473; ArchitectureTests
79/79; web Vitest 213/213; solution build 0 warnings/errors; web production
build, lint and `api:check` passed; EF model has no pending changes. Security
and Tester/Reviewer independently ACCEPT with no open findings. The reviewer
found a tab-switch pagination-state issue and Security found a missing HTTPS
guard during review; both fixes were re-tested before acceptance.

M1 verification limitation: the Admin PostgreSQL fixture verifies the empty
aggregate baseline, the seeded plan catalog, authorization and response shape;
positive-count projection coverage is strengthened by owner-module query
contracts and invitation effective-state unit tests, but a populated
cross-module aggregate fixture remains a useful closure follow-up.

P9-M7 phase closure: Tester/Reviewer, Security and Architect independently
ACCEPT with no open findings. Full verification passed: solution build with
0 warnings/errors; ArchitectureTests 79/79; UnitTests 482/482;
PostgreSQL IntegrationTests 501/501; web tests 251/251; OpenAPI/client
`api:check`, lint, typecheck and production build. The existing non-blocking
M1 coverage limitation above remains tracked. No push, VPS connection,
deployment, or live provider operation was performed.

## Milestone Bağımlılık Grafiği

```text
M1 ──┬──> M2 ──> M3 (PD-11)
     ├──> M4
     ├──> M5
     └────────────────────┬──> M6 ──> M7
                           │
```

M2, M4, M5, M1'den sonra farklı yüzeyleri (ban / settings / operasyonel
durum) etkilediği için paralel yürütülebilir. M3 yalnız M2'ye ve PD-11
kararına bağımlıdır. M6 (Admin UX) hepsinin üzerine kurulur.

## Beklenen Uzman Rolleri

- **Architect:** ADR-0002/0004 sınırları, governance contract review.
- **Backend + Database:** aggregate read model, ban lifecycle, audit.
- **Security:** MFA-complete policy, ban session revocation, least-
  privilege projection, recovery runbook.
- **Frontend + UI/UX:** Admin shell, responsive/a11y.
- **Tester:** first-factor vs MFA access, recovery code single-use, ban
  active-session/public shutdown, hard ceiling, audit redaction testleri.
- **Reviewer:** faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture testleri ve gerçek PostgreSQL Testcontainers integration
  testleri geçer.
- Saf unit test kapsamı zorunlu değildir (Fatih'in kararı: manuel/UI testi
  tercih edilir); izole edilmesi güç, kritik mantık için (örn. hard-ceiling/
  value-type/concurrency guard'ları) hedefli unit test eklenebilir ama şart
  değildir.
- Ban oluşturmanın aktif session'ı ve public delivery'yi gerçekten
  kapattığını kanıtlayan integration testleri geçer.
- Hard-ceiling ve concurrency testleri geçer.
- 320 CSS px, 200% zoom, keyboard ve automated a11y testleri geçer.
- CI clean checkout'ta bu kontrolleri tekrarlar.

## Faz Tamamlanma Kriterleri

- Tüm kritik mutasyonlar audit edilmiştir.
- Admin'in private guest-data'ya navigasyon/bypass'ı yoktur.
- PD-11 varsayılmamıştır — ya Fatih tarafından açıkça cevaplanmıştır ya da
  ilgili milestone henüz başlamamıştır.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer,
  Architect onayı vardır. Açık Critical/High bulgu yoktur.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; Admin rolleri/policy'leri, recovery
runbook sahibi, audit event'leri, PD-11 sonucu ve run evidence günceller.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 9 uygulama izni değildir. Faz 9, ancak öncesindeki
fazlar fiilen tamamlanıp bağımsız doğrulandıktan **ve** Fatih bu planı
(veya doğrulanmış önceki fazlar sonrası güncellenmiş halini) açıkça
onayladıktan sonra başlayabilir. Faz 9'un tamamlanması, çalıştırılabilir
bir platform ve son privacy/release-candidate kapanışını mümkün kılar.
