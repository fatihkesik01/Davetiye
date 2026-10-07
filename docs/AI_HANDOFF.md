# AI Handoff — Current State

**Last updated: 2026-10-07**

Living snapshot only. History lives in Git and each `docs/PHASE_N_PLAN.md`.
Independently verify this against `git status`/`git log`, source and real
checks before trusting it (`docs/AI_WORKFLOW.md` §1/§11).

## Current Phase Status

- **Phases 0–10: COMPLETED (11/12).** Milestone tables, attribution and
  per-phase evidence are in `docs/PHASE_N_PLAN.md`.
- **Phase 11 — Production Readiness & Business Launch: NOT STARTED.** It
  requires Fatih's separate explicit approval. Plan: `docs/PHASE_11_PLAN.md`.
- No active implementation phase.

## Latest Verification (2026-10-07, local)

- Release solution build: 0 warnings / 0 errors.
- UnitTests 491/491; ArchitectureTests 81/81.
- EF model: no pending changes against the latest migration.
- Web: lint, typecheck, API-client check, Vitest 269/269, `npm audit` 0.
  A load-sensitive Vitest timeout (lazy route chunks exceeding the 1s
  Testing Library default) was fixed by raising `asyncUtilTimeout` in
  `src/web/src/test/setup.ts`.
- Media-ingress Worker: 52/52, typecheck.
- Last full PostgreSQL IntegrationTests run (P10-M7): 527/527; Playwright
  177/177. Not re-run in this session.

## Deferred to Phase 11 (mandatory gates)

- Clean-checkout GitHub Actions CI on the Phase 2–10 code.
- VPS deployment (`docs/DEPLOYMENT.md`; never touch Lora).
- Real provider acceptance: Cloudflare R2/Images/Stream (incl. in-flight
  upload at expiry and legacy 15-min capability grace), iyzico (incl.
  refund/dispute/chargeback source verification), Resend, Google OAuth.
- Organization provider-side cancellation adapter/worker (account deletion
  currently stops local auto-renewal and queues a cancellation intent only;
  no automatic refund).
- Actual browser-UI 200% zoom and manual screen-reader/contrast review.
- Legal: exact retention periods, tombstone-ID acceptability, audit
  retention (no automatic audit deletion before this decision), refund/tax
  wording.

## Open Technical Debt (Low)

- Trusted-proxy CIDR in Compose is broader than least privilege; narrow it
  when the production proxy topology is fixed.
- Session projection and Creator draft endpoints lack dedicated
  account-partitioned rate-limit policies.
- `/auth/google/complete` returns a distinct 403 for a Super Admin email
  (minor enumeration signal).
- Google return URL uses a safe relative-path heuristic instead of a literal
  route allowlist; no open redirect is possible.
- `apk upgrade` in images reduces build reproducibility; consider digest
  pinning with a controlled refresh cadence.

## Runbooks

- Admin lost-MFA recovery (platform owner only): `docs/ADMIN_MFA_LOST_FACTOR_RECOVERY.md`.
- Terminal media deletion: `dotnet run --project tools/Davetiye.MediaDeletionRetry -- --list --limit 100`;
  requeue with `-- --retry <message-guid> --confirm`.
- Backup/restore: `docs/BACKUP_RESTORE_RUNBOOK.md`; migrations:
  `docs/DATABASE_MIGRATIONS.md`.

## Next Action

Await Fatih's explicit Phase 11 approval and the owner decisions listed in
`docs/PHASE_11_PLAN.md` (domain, support/sender addresses, legal texts,
provider accounts). Do not deploy before that approval.
