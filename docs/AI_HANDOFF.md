# AI Handoff — Current State

**Last updated: 2026-09-29** (session: Codex, Phase 1 closure and Phase 2 planning)

This is a living snapshot, not a history log. It reflects what a
verification pass found on the date above. **Before acting on anything in
this file, verify it against the actual repository per `docs/AI_WORKFLOW.md`
§1/§11** — do not trust it, or any prior agent's "done" claim, without
re-checking `git log`/`git status`, re-running builds/tests, and comparing
against `src/`, `tests/`, and `docs/adr/`. Permanent history belongs in Git
and in the dated phase documents (`docs/PHASE_1_PLAN.md`,
`docs/PHASE_1_EXECUTION.md`), not here.

## Current Approved Phase

**Phase 1 — Repository and Production Foundation**
(`docs/PHASE_1_PLAN.md`, approved per `docs/PHASE_1_EXECUTION.md`).

Phase 1 builds repository/build/CI/security/observability foundation only.
It explicitly excludes invitation CRUD, RSVP/Memories/Gift business logic,
real Cloudflare/iyzico/Resend integration, full Creator/Admin UI, and any
production deployment.

## Current Phase Status

**In progress, awaiting remote CI evidence.** M0–M9 are independently
verified complete after the interruption recovery. M10's workflow has been
implemented and locally smoke-tested, but its first GitHub Actions run must
pass after the approved push before M11 can close the phase. Earlier
paragraphs that say M4–M11 were unstarted are historical and not current.

**Progress ratio (per `docs/AI_WORKFLOW.md` §13.2, counted from the
milestone units listed below, not estimated):** 15/17 milestone units
complete — M0, M1, M1U, M2A, M2B, M3, M4, M5A, M5B, M6a, M6b, M7A, M7B, M8,
M9 — ≈88%. Remaining: M10 and M11. This counts milestone
units, not effort or lines of code — later milestones (M8–M11) are not
necessarily the same size as earlier ones, so this ratio is a completion
count, not a time/effort forecast.

## Last Completed Phase

**Phase 0 — MVP Technical Baseline** (`docs/PHASE_0_BASELINE.md`, status
"Tamamlandı; açık ürün kararları decision register'da" — accepted, with
explicitly open product decisions tracked separately, see below). Baseline,
threat model (`docs/THREAT_MODEL.md`), UX flows (`docs/UX_FLOWS.md`) and all
seven ADRs under `docs/adr/` are marked Accepted.

## Active Milestone

**M7B — Auth route-guard acceptance.** M7A is now verified complete; M7B
is unblocked and must add no product business UI while it proves the accepted
Public/Creator/Admin guard matrix.

## Completed Milestones (independently verified 2026-09-28)

- **M0** — Baseline/decision guard. ADR index consistent, all 7 ADRs
  Accepted, PD-01–PD-13 correctly still unresolved.
- **M1** — Repository/dependency contract. `Davetiye.slnx`,
  `Directory.Build.props`/`Directory.Packages.props`, `global.json` exist;
  project references only flow in the allowed direction (enforced by
  architecture tests).
- **M1U** — UX foundation contract. Independently reviewed and **Accepted**
  2026-09-29: all 9 required deliverables present and substantive, no scope
  violation, no PD-01–13 assumption, consistent with
  `docs/PRODUCT.md`/`docs/UX_FLOWS.md`/`docs/PHASE_0_BASELINE.md`
  everywhere cross-checked. Two Low, non-blocking notes recorded (see
  `docs/PHASE_1_EXECUTION.md`'s M1U section).
- **M2A** — .NET backend skeleton. `dotnet build Davetiye.slnx` succeeds
  cleanly (0 warnings/errors); `Davetiye.ArchitectureTests` passes **66/66**
  (grew from 62 previously recorded, all still green).
- **M2B** — React/TypeScript bootstrap. `npm run build` produces a
  production `dist/`; `npm test -- --run` passes 3 test files / 6 tests.
  Shell only contains error boundary, loading/not-found states and i18n
  init — no business UI, consistent with scope.
- **M3** — PostgreSQL/config/migration foundation. **Newly verified this
  session** (previously "status unknown, needs reconciliation"):
  - `Davetiye.UnitTests` — 8/8 passed, including
    `PersistenceConventionTests.PostgreSql_model_applies_foundation_conventions`,
    which the prior handoff recorded as failing. Already fixed in the
    working tree; no action needed.
  - `Davetiye.IntegrationTests` — 3/3 passed against a real PostgreSQL
    instance via Testcontainers (Docker Desktop is reachable in this
    environment, unlike the prior session). Covers empty-DB migration,
    idempotent re-apply, and targeted-then-latest upgrade path.
  - `ModelBuilderExtensions.cs` implements the required conventions:
    snake_case naming, `uuid` for `Guid`, `timestamp with time zone` for
    `DateTimeOffset` (throws on raw `DateTime`), money precision/scale,
    `Revision` as `Int64` concurrency token, `DeletedAt`/`PurgeAfter`
    soft-delete query filter.
  - `tools/Davetiye.DatabaseMigrator/` is a separate console executable;
    `Program.cs` does not call `Database.Migrate`, matching the required
    separation of migration from API startup.
- **M4** — API runtime contract. Implemented and independently verified
  this session (build clean; ArchitectureTests 66/66; UnitTests 20/20;
  IntegrationTests 12/12 including 9 new tests against real PostgreSQL) plus
  an independent Reviewer pass (no Critical/High findings) and Security pass
  (no Critical/High findings). `/api/v1` prefix, sanitized ProblemDetails,
  correlation ID, OpenAPI, genuinely-separate `/health/live`+`/health/ready`,
  Kestrel-level body limit with hard ceiling, fail-closed trusted-proxy
  config. Full detail and the resulting Medium/Low technical debt items are
  recorded in `docs/PHASE_1_EXECUTION.md`'s "M4 — API runtime contract"
  section — do not re-derive, read that section.
- **M5A** — Identity/Account/typed Plan/Entitlement/SystemSetting schema.
  Implemented and independently verified this session (build clean;
  ArchitectureTests 66/66; UnitTests 49/49; IntegrationTests 14/14,
  including a real-Postgres proof that "at most one Account per Identity
  user" is enforced by an actual DB unique constraint) plus independent
  Reviewer and Security passes (both no Critical/High findings). Schema
  only — no auth endpoints, no UserManager/SignInManager wiring, no MFA, no
  EffectiveEntitlementResolver business logic. Full detail and the
  resulting Medium technical debt items (write-time AccountId validation
  needed before real grant issuance; entitlement image/video size hard
  ceilings should be tightened before Plan seed data; BanRecord cascade
  delete should be revisited before any Account hard-delete path exists)
  are recorded in `docs/PHASE_1_EXECUTION.md`'s "M5A" section.
- **M5B** — Inbox/outbox worker primitives (Integration Foundation module).
  Implemented and independently verified this session (build clean;
  ArchitectureTests 66/66; UnitTests 70/70; IntegrationTests 19/19,
  including a real-Postgres proof of idempotent-insert rejection and a
  genuine 6-way concurrent-claim test proving no double-claim/no lost row)
  plus independent Reviewer and Security passes. One real Medium bug found
  by both passes (a bare `catch` swallowing `OperationCanceledException` in
  `BatchMessageWorker`, misreporting shutdown as a handler failure) was
  fixed and re-verified before closing this milestone — all three suites
  still green after the fix. Full detail and remaining Low technical debt
  in `docs/PHASE_1_EXECUTION.md`'s "M5B" section.
- **M6a** — Core email/password auth + cookie/CSRF/CORS/rate-limit/Data
  Protection baseline. Implemented (across an interrupted first session
  and a completing second session), then independently re-verified,
  Reviewer- and Security-reviewed, and had every real finding from those
  passes fixed and re-verified (final: build clean; ArchitectureTests
  66/66; UnitTests 87/87; IntegrationTests 30/30). Full detail, including
  a High-severity dormant finding recorded as a hard prerequisite for
  whichever future milestone adds ban creation, in
  `docs/PHASE_1_EXECUTION.md`'s "M6a" section — read that before touching
  ban enforcement or Admin ban-creation later.
- **M6b** — Google OAuth + Admin bootstrap + TOTP/MFA. Implemented
  (building on M6a without redesigning it — one narrow, justified
  exception to shared cookie-validation code, independently confirmed
  correct and a no-op for non-2FA sessions), then independently
  re-verified, Reviewer- and Security-reviewed, and had every real finding
  fixed and re-verified (final: build clean; ArchitectureTests 66/66;
  UnitTests 94/94; IntegrationTests 39/39). One dormant High (Google
  sign-in had no ban check) was fixed immediately in this same pass —
  unlike M6a's structurally-blocked dormant High, this one had no
  blocker. Full detail, including the two remaining Low items, in
  `docs/PHASE_1_EXECUTION.md`'s "M6b" section.

## Remaining Milestones

Confirmed **not started** by direct inspection (not merely "waiting" in a
table) on 2026-09-29:

- **M7B** — Auth route-guard acceptance. Depends on verified M6/M7A; not
  started.
- **M8** — Authorization/architecture/observability verification (BOLA
  fixture, extended architecture tests, redaction tests).
- **M9** — Container/Nginx/backup foundation. No `deploy/` directory exists.
- **M10** — CI release gate. No `.github/workflows/` exists.
- **M11** — Independent phase closure (Reviewer/Security/Tester/Architect
  sign-off + Phase 2 readiness report).

## Current Blockers

None currently open.

**Still open, not a blocker to continuing, but unresolved:** nothing is
committed to Git yet beyond the initial commit (see below) — this is a
pending decision, not a defect.

## Unresolved Product Decisions

All of PD-01 through PD-13 remain **unresolved** (full detail:
`docs/PHASE_0_BASELINE.md` §12). None are closed by Phase 1 foundation work;
none should be assumed by any implementation choice:

PD-01 (Free grant reissuance), PD-02 (Org subscription end effect on active
windows), PD-03 (`maxPublishDays` reduction vs. started window), PD-04
(boolean entitlement downgrade behavior), PD-05 (Scheduled/Paused vs. active
quota), PD-06 (Creator/Guest media quota sharing), PD-07 (Scheduled
cancel/reschedule/publish-now), PD-08 (trash restore target state), PD-09
(Expired → future Scheduled), PD-10 (gift optional contact fields/retention),
PD-11 (admin unban), PD-12 (renewal/cancel/refund/checkout flows), PD-13
(Scheduled autosave publication semantics).

## Known Technical Debt

- M1U Low items (owner: whoever next touches the document): microcopy/state
  tables don't carry a per-row requirement/recommendation tag; no
  ban-confirmation copy example exists yet (low priority — no ban-creation
  endpoint exists anywhere in the codebase). Full detail in
  `docs/PHASE_1_EXECUTION.md`'s M1U section.
- M4 Medium/Low items (owner Backend/Security): trusted-proxy loopback
  default shipped in the environment-agnostic base `appsettings.json`
  (confirm intended vs. dev leftover); ProblemDetails `Instance` echoes raw
  request path (revisit once a sensitive route exists); `/openapi/v1.json`
  has no environment guard (M6 should explicitly decide prod exposure); no
  end-to-end test proves Kestrel's body-limit 413 or forwarded-header trust
  rejection (only unit-level validation is tested; forwarded-header
  end-to-end testing explicitly deferred to M6). Full detail in
  `docs/PHASE_1_EXECUTION.md`'s M4 section.
- M5A Medium items (owner Backend/Database/Product): write-time `AccountId`
  validation required before any milestone actually issues a real
  `AccountPlanGrant`; `EntitlementCatalog` image/video size hard ceilings
  should be tightened before Plan seed data is finalized;
  `BanRecordConfiguration`'s cascade-delete-on-Account should be revisited
  before any Account hard-delete path is built. Full detail in
  `docs/PHASE_1_EXECUTION.md`'s M5A section.
- M5B Low items (owner: whichever milestone first writes a real payload):
  inbox/outbox `Payload` columns are unbounded `text` with no redaction
  hook — the first real Payments/Media/Notifications handler must decide
  what's safe to persist and set an explicit max length. Full detail in
  `docs/PHASE_1_EXECUTION.md`'s M5B section.
- **M6a High item — hard prerequisite, not generic debt (owner: whichever
  milestone first adds ban creation)**: creating a `BanRecord` does not
  currently revoke an already-issued session (nothing updates the
  Identity user's security stamp). Dormant/not exploitable today (no
  ban-creation endpoint exists anywhere yet), but the first milestone
  that adds one MUST call `UpdateSecurityStampAsync` in the same
  operation, or banning becomes a live no-op against active sessions.
  Full detail in `docs/PHASE_1_EXECUTION.md`'s M6a section.
- M6a Medium/Low items (owner: various, see full detail in
  `docs/PHASE_1_EXECUTION.md`'s M6a section): Production `RegisterAsync`
  can now create a real account but return 500 if email sending fails
  (reachable for the first time now that email sending fails closed
  instead of silently no-op-succeeding); no rate limiting on confirm-
  email/reset-password/antiforgery-token; M4's forwarded-header-trust
  test gap still unmet; minor login/reset-request timing side-channels.
- M6b Low items (owner: whichever milestone next touches this code, see
  full detail in `docs/PHASE_1_EXECUTION.md`'s M6b section): Google
  sign-in's return-URL safety relies on a relative-path heuristic rather
  than the literally-specified route allowlist (functionally safe today,
  no open redirect possible); `bypassTwoFactor: true` in Google sign-in
  depends on a "Google-linked accounts are never Super Admin" invariant
  that isn't enforced in code, only true by the absence of an
  account-linking flow — must be re-verified before any future
  account-linking milestone.
- No CI yet (M10 not started), so none of the above is currently enforced
  automatically on change.
- Nothing is committed to Git beyond the initial commit; all Phase 0/Phase 1
  work is in the working tree. A commit strategy decision (one commit per
  milestone vs. batched) has not been made.

## Required External Dependencies / Credentials

None of these are present in the repository (correct — per
`docs/DEPLOYMENT.md`, nothing is deployed and no secrets are committed).
They will be needed before the corresponding Phase 1/2 milestones can go to
production, not before Phase 1 foundation work:

- Production domain (not yet purchased) + HTTPS, required before Google
  production sign-in can be enabled (`docs/ARCHITECTURE.md` §2,
  `docs/DEPLOYMENT.md`).
- iyzico merchant credentials (development uses `FakePaymentGateway`).
- Resend production API key (development uses a fake/local sender).
- Cloudflare R2 / Images Transformations / Stream credentials.
- Docker daemon access — **now confirmed reachable** in this environment
  (Docker Desktop, WSL2 backend), needed for `Davetiye.IntegrationTests` and
  M9's container/Compose runtime evidence requirement.

## Last Verification Summary (2026-09-29, interruption recovery)

Commands actually run, with real results — not claims:

- `dotnet restore Davetiye.slnx` (outside the restricted NuGet sandbox), then
  `dotnet build Davetiye.slnx --no-restore --nologo` → **succeeded**, 0
  warnings/errors.
- Architecture tests → **66/66 passed**; Unit tests → **94/94 passed**.
- A fresh IntegrationTests run was started against Docker/Testcontainers but
  did not return a final result in the command window before interruption;
  do not treat the prior 39/39 claim as re-verified in this session.
- `npm run api:verify` against the locally running backend OpenAPI document,
  `api:check`, lint, typecheck and build → **succeeded**. Frontend Vitest →
  **4 files / 8 tests passed**; lazy Public/Creator/Admin chunks were emitted.
- `git log` → single commit on `main`; everything else uncommitted (no
  change from prior session).
- `docker info` → Docker Desktop reachable (WSL2 backend, API 1.49).
- Independent Reviewer and Security passes on M4, M5A, M5B, M6a, and M6b
  → all ten completed. M6a and M6b each surfaced one dormant High: M6a's
  (ban doesn't revoke an active session) has no fix available yet and is
  recorded as a hard prerequisite for the future ban-creation milestone;
  M6b's (Google sign-in had no ban check) had no such blocker and was
  fixed immediately. Every other real finding from both milestones'
  passes was fixed and re-verified, not just logged.

## Next Recommended Action

1. Implement **M7B — Auth route-guard acceptance** against the approved
   route/authorization matrix, without adding out-of-scope business UI.
2. Whichever milestone first adds an Admin ban-creation endpoint (likely
   a later Administration milestone, since M6b deliberately built no real
   Admin business endpoints) MUST also wire session revocation on ban —
   see the M6a High finding — and must apply the same ban check to any
   Google-sign-in-adjacent code path if one is added, consistent with the
   M6b fix.
3. Decide when/whether to make the first git commit(s) for the substantial
   uncommitted work currently in the working tree — not yet decided.

## Next Phase Plan Status

**Not started, and should not be.** Phase 1 is not yet verified complete
(§7 of `docs/AI_WORKFLOW.md`) — M7B through M11 remain unimplemented — so per
§8–§9 no Phase 2 plan should exist yet, and none does.
`docs/PHASE_TEMPLATE.md` is a reusable template for whenever Phase 2
planning is actually appropriate; it is not a Phase 2 plan itself.
