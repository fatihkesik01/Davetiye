# AI Handoff — Current State

**Last updated: 2026-10-12**

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

## UI workstream (Fatih request 2026-10-08) — deployed to production at `fe62945`; UI-6 polish committed, awaiting Fatih's local review before deploy

Implemented: shared `SiteHeader` on landing, catalog, auth, legal and the
Creator/Admin shell (not on `/davetiye/*`); session-aware header (anonymous /
Creator / Admin / MFA-setup); tr/en UI language, six palettes (incl. gold) and
system/light/dark appearance stored per account (`GET/PUT /api/v1/account/preferences`,
migration `P11UiPreferences`) and applied on public pages; theme tokens for
landing/catalog/auth/legal; landing 200%-zoom overflow fixed. Preset avatars
(12 keys, nullable `preferred_avatar`, migration `P11AvatarPreference`) are
implemented in the picker on `/panel/hesap` and the account drawer and shown in
the header account button; photo upload only after the Cloudflare media gate
(P11-M6). Fatih views the frontend locally with a Vite proxy to the VPS API, so
backend/DB changes must be deployed for his local UI to see them.

## Local zarf template work-in-progress (2026-10-12)

- Fatih is refining the eight decor themes and envelope presentation in the
  local `/sablonlar/zarf-davetiye` page. Session context and the next checks are
  in [`WORK_DRAFT_1.md`](WORK_DRAFT_1.md).
- Uncommitted changes include theme artwork/keys, editor and renderer mapping
  for `garden` and `accentColor2`, the backend invitation contract allowlist,
  and envelope styling. Check `git status` before changing files; do not discard
  the existing work.
- Latest requested layout: each side illustration must begin at the envelope's
  left/right edge and extend outward. CSS positioning was just changed but has
  **not** been visually confirmed after the latest screenshot. Check desktop and
  narrow mobile views with `papatya-kelebek` and theme 8 (`yildizli-gece`).
- Local Vite modules returned HTTP 200 and `git diff --check` passed. No build,
  typecheck or tests were run. No VPS connection or deployment occurred here.

## Second template work-in-progress (2026-10-12)

- Public demo URL: `/sablonlar/kapi-davetiye`; its Turkish display name is
  “Kapı” and its English display name is “Gate”. The previous `/sablonlar/gate`
  and `/sablonlar/dantel-davetiye` URLs remain aliases and canonicalize to the
  Turkish route.

- Fatih approved a separate second template after Zarf Davetiye, based on the
  lace-and-pearl folding invitation reference. It is keyed `dantel-davetiye`,
  displayed as “Kapı” / “Gate”, and uses renderer version 1. The stable key remains for
  existing draft and invitation compatibility. It does not replace or
  alter Aşk Mektubu.
- A separate renderer, preview image, registry entry, demo content, and
  Creator/demo look pickers are present. It reuses the existing draft fields
  `accentColor`, `accentColor2`, `ambiance`, and `garden`; no schema change was
  made. The catalog initializer will add the template row when the updated API
  starts against its configured database.
- Current local `/api/v1/templates` response does not contain the new key. A
  development-only catalog fallback now inserts the code-owned template after
  Zarf Davetiye so it can be previewed locally before DB reconciliation. The
  running Vite process is the only local listener; it proxies `/api` elsewhere,
  so do not assume the Creator save flow supports this key yet.
- The full-page Dantel demo now uses two viewport-sized doors, escapes the
  public shell's content width, keeps lace artwork proportional on tall
  screens, and shortens open/close controls. It reveals a full-page invitation
  scene. This CSS change has not yet been reviewed in a browser at desktop/
  mobile sizes.
- Gate customization now offers seven decorated themes (Saray Kapısı, Japon
  Bahçesi, Akdeniz Avlusu, Buz Sarayı, Gül Serası, Venedik Karnavalı, Yıldız
  Geçidi) plus “Yok”. Each added theme has its own SVG gate scene, leaf ornament,
  picker icon, and open-invitation border artwork; fabric, embroidery/seal tone,
  and page mood still use the shared customizers. A realism pass added eight
  compressed WebP surface textures (royal oak, Japanese lacquer, Mediterranean
  stone, ice, greenhouse iron/glass, Venetian velvet, starry enamel, and plain
  oak), plus inset panels/grain/knobs for the plain gate. The palace theme
  uses dark oak panels, an arched portal, iron straps and a crowned fleur-de-lis
  shield. The full-page demo opens with the palace theme and gold metalwork;
  existing saved garden values remain renderable. Cover fabric and
  seal/lettering colors remain stored through the existing allow-listed draft
  keys. The local API contract now accepts `saray-kapisi` for `garden`.
- The Japanese gate was visually reviewed closed and open at desktop width;
  all eight texture mappings and asset responses were checked at 390px width,
  with no page errors or horizontal overflow. `npx vite build` passes. The
  combined `npm run build` is blocked in `tsc -b` by pre-existing stale `flora`
  references in invitation and picker tests/types. The
  registry test still expects the old counts (17 renderer pins / 9 template
  keys) and its snapshots do not include the new renderer; update this test
  fixture when test work is authorized. No build, typecheck or tests were run.
- No VPS connection, database startup/reconciliation, or deployment occurred.
  Local catalog visibility may require the local API to restart; verify its
  database target first and follow `DEPLOYMENT.md` before any VPS access.

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

Davetiye deployed 2026-10-08 (`/opt/davetiye`, ports 5052/8082; release `fe62945`
incl. migrations `P11UiPreferences` and `P11AvatarPreference`). Pre-migration
backup: `/opt/davetiye/backups/davetiye-pre-ui-prefs-20261008T170023Z.dump`
(Davetiye DB only, local to the VPS until the off-site backup in P11-M7). Lora
healthy after every step. Host-level items reported to Fatih, not changed because
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

Resume from `docs/PHASE_11_PLAN.md` ("İş Takip Listesi"). The UI/preferences/
avatar release is live. Open product questions for Fatih: an on-device language/
dark-mode toggle for anonymous visitors, a header "Ücretsiz başla" CTA, the
duplicated "Hesap tercihleri" heading on `/panel/hesap`. Then the remaining
owner items (Google OAuth, Cloudflare media, off-site backup, iyzico sandbox). Company/tax/legal/
iyzico live stay last. Never touch Lora, including its backups.
