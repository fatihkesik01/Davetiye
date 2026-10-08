# AI Handoff — Current State

**Last updated: 2026-10-08**

Living snapshot only. History lives in Git and each `docs/PHASE_N_PLAN.md`.
Independently verify this against `git status`/`git log`, source and real
checks before trusting it (`docs/AI_WORKFLOW.md` §1/§11).

## Current Phase Status

- **Phases 0–10: COMPLETED (11/12).** Milestone tables, attribution and
  per-phase evidence are in `docs/PHASE_N_PLAN.md`.
- **Phase 11 — Production Readiness & Business Launch: IN PROGRESS** (Fatih
  approved 2026-10-08). Ordered tracker: `docs/PHASE_11_PLAN.md`.
- Deployed to the VPS and live (unannounced) at `https://kutlio.com`; see
  `docs/DEPLOYMENT.md`. Brand is "Kutlio"; transactional email via Resend is
  live (2026-10-08). Google, media and payments are not enabled yet. Secrets
  are entered by Fatih on the server via `/opt/davetiye/set-secret.sh`; an
  agent never handles live secret values.

## Latest Verification (2026-10-08, local)

- Release solution build 0 warnings; UnitTests 493/493; ArchitectureTests 81/81;
  EF model has no pending changes.
- Web: lint (0 warnings), typecheck, API-client check, build; Vitest 296/296;
  Playwright 252/252 across desktop, 320px and 200% zoom.
- PostgreSQL integration tests cannot run locally (Docker unavailable); CI runs
  them. The new `AccountUiPreferencesEndpointsTests` and
  `PublicPlanCatalogEndpointsTests` have first run on CI.
- Security: public plan catalog ACCEPT (endpoint is always `no-store`); account
  UI preferences endpoint ACCEPT with its integration tests added.

## UI workstream (Fatih request 2026-10-08)

Implemented: shared `SiteHeader` on landing, catalog, auth, legal and the
Creator/Admin shell (not on `/davetiye/*`); session-aware header (anonymous /
Creator / Admin / MFA-setup); tr/en UI language, five palettes and
system/light/dark appearance stored per account (`GET/PUT /api/v1/account/preferences`,
migration `P11UiPreferences`) and applied on public pages; theme tokens for
landing/catalog/auth/legal; landing 200%-zoom overflow fixed. Avatar slot in the
account button is a generic icon; avatar presets are the next item (photo upload
only after the Cloudflare media gate, P11-M6). Fatih views the frontend locally
with a Vite proxy to the VPS API, so backend/DB changes must be deployed for
his local UI to see them.

## Deferred to Phase 11 (mandatory gates)

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

## VPS

Davetiye deployed 2026-10-08 (`/opt/davetiye`, ports 5052/8082). Lora healthy
after every step. Host-level items reported to Fatih, not changed because
the host is shared with Lora: reboot pending (newer kernel installed), 5
pending security updates, sshd password authentication enabled and port 22
open alongside 22222, stale ufw allow rules for 8080/5050.

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

Resume from `docs/PHASE_11_PLAN.md` ("İş Takip Listesi"). Order: deploy the
verified UI/preferences release (backup Davetiye DB first; migration
`P11UiPreferences` is additive), then avatar presets (backend column +
migration + settings UI), then the remaining owner items. Company/tax/legal/
iyzico live stay last. Never touch Lora, including its backups.
