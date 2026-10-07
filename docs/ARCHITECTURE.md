# Davetiye — Accepted MVP Technical Decisions

Status: **Accepted**  
Last updated: **2026-09-28**

This document records confirmed technical constraints for the MVP. It
does not replace `docs/PRODUCT.md`; product behavior and scope remain
defined there. When the documents appear to conflict, implementation
must stop until the product source of truth is clarified.

Detailed implementation boundaries are recorded in:

- `docs/PHASE_0_PLAN.md`
- `docs/THREAT_MODEL.md`
- `docs/UX_FLOWS.md`
- `docs/PHASE_1_PLAN.md` (§10 carries the UX Foundation contract)
- `docs/adr/`

## 1. Application Shape

- The application is a modular monolith.
- The frontend is a single responsive React application.
- The backend is a single ASP.NET Core Web API application.
- PostgreSQL is the transactional database.
- Modules share one deployment and one database but keep explicit
  application/domain boundaries.
- Microservices, a message broker and Kubernetes are not MVP
  dependencies.

## 2. Accounts and Authentication

- MVP organization accounts are single-user accounts. Memberships,
  team invitations, employee roles and workspaces are out of scope.
- Email/password and Google sign-in are required MVP authentication
  methods.
- A raw non-localhost IP cannot be registered as a Google web OAuth
  redirect host. The IP-only deployment remains usable with
  email/password; Google login is tested on localhost and enabled for
  production after a domain and HTTPS are configured. See Google's
  [OAuth client validation rules](https://support.google.com/cloud/answer/15549257).
- Email verification and password reset are required.
- Browser authentication uses secure server-managed sessions/cookies;
  credentials are not stored as plain tokens in localStorage.
- Super Admin accounts are created through a controlled bootstrap,
  never public registration, and require MFA.
- Super Admin MFA uses an authenticator-app TOTP factor plus one-time
  recovery codes. SMS is not introduced.

## 3. Invitation Publication

- Supported lifecycle states are Draft, Scheduled, Active, Paused and
  Expired, with soft deletion/trash handled separately.
- Immediate publication becomes Active; scheduled publication becomes
  Active at its configured start time.
- Publication entitlement time is calendar-based. Pausing does not stop
  or extend the entitlement window.
- An Expired invitation can be reactivated after a new eligible grant.
  Its identity, content and public URL are preserved.
- Public access checks the effective time window synchronously even if a
  background lifecycle job is delayed.

## 4. Plans, Entitlements and Settings

- Plans and business limits are stored in the database, not hardcoded
  in application code.
- Typed plan features/entitlements cover publication days, active
  invitation count, media limits, RSVP limits and module/template
  access.
- Super Admin can change supported plan and setting values through the
  administration UI; controlled DB operations remain possible.
- Plan changes can affect existing accounts. A reduced limit never
  automatically deletes existing data; new usage is blocked until the
  account is back within the effective limit.
- Security-sensitive values have backend hard ceilings in addition to
  configurable business limits.
- Initial commercial seed values are defined in `docs/PRODUCT.md` and
  remain editable without an application deployment.

## 5. Provider Boundaries

### Media

- Photos are stored in private Cloudflare R2 and delivered through
  Cloudflare Images Transformations. Before any photo reaches durable
  provider storage, its bytes pass through a Cloudflare Worker using the
  Images binding to decode and re-encode as WebP; the Worker writes only
  the transformed output to R2. This output format discards source EXIF/GPS
  metadata. Original uploads are never written to R2 or hosted Images
  storage.
- Videos use Cloudflare Stream.
- The browser uploads photo bytes to the Cloudflare Worker, not to the
  Davetiye API/VPS. The API authorizes an upload intent and issues a
  short-lived capability scoped to one asset/key; the Worker verifies that
  capability and enforces the actual streamed byte ceiling before storing
  transformed output. Client metadata and completion claims remain
  untrusted. Videos may keep direct browser-to-Stream upload after API
  authorization.
- Media bytes are not stored on the VPS or in PostgreSQL. PostgreSQL
  stores ownership, provider identifiers, metadata and processing state.
- Cloudflare credentials stay server-side. The image Worker uses narrowly
  scoped Images and R2 bindings; raw provider credentials are not sent to
  the browser.
- The image upload Worker requires the Cloudflare Images binding, which
  requires a paid Images subscription. Cloudflare account-plan request-body
  limits and Worker memory/CPU limits are deployment prerequisites: they
  must support the configured hard ceilings, and do not replace the
  Worker's streamed byte counter. Per Fatih's 2026-10-04 decision, account
  setup, account-specific limit review, and real-provider acceptance are
  deferred to Phase 11 P11-M6/M8. Local contract tests do not verify those
  account limits; media remains unavailable to users until the Phase 11 gate.

### Payments

- Production payment provider: iyzico.
- Development may use a FakePaymentGateway until merchant credentials
  are available.
- The application boundary is provider-neutral (`IPaymentGateway` or an
  equivalent port); provider DTOs do not enter the domain model.
- Webhook verification, replay protection/idempotency and entitlement
  activation are mandatory parts of the integration.
- Merchant credentials are runtime secrets and are never committed.

### Transactional Email

- Production provider: Resend.
- Development may use a fake/local sender.
- The application boundary is provider-neutral (`IEmailSender` or an
  equivalent port); changing providers must not change domain behavior.

## 6. Privacy, Analytics and Deletion

- MVP analytics records aggregate page/total views only. It does not set
  an anonymous visitor identifier for unique visitor tracking.
- Data minimization is the default. Required service consent and
  marketing consent are separate concerns.
- Retention settings are configurable where appropriate.
- The model keeps ownership boundaries suitable for future data export,
  but data export itself is not added to MVP scope by this decision.
- After configurable trash retention, permanent invitation purge removes
  invitation content, RSVP, memories, gift data, statistics and related
  Cloudflare media.
- Payment/invoice and required audit records can remain separately under
  applicable operational or legal retention.
- Account deletion is confirmed by email; confirmation immediately revokes
  active sessions, closes public access and starts permanent deletion of the
  account and invitation/guest content. Provider media deletion remains
  retryable and independently verifiable.
- Exact legal/operational retention periods are deferred to Phase 11 review.
  Until that decision, payment, audit, log and backup records are not
  automatically deleted. Service notice is separate from a default-off
  marketing opt-in; optional tracking cookies are not used in the MVP.

## 7. Deployment and Operations

- Target infrastructure is the existing Hostinger VPS using Docker
  Compose and Nginx.
- The application must initially work by IP. Domain, HTTPS and public
  base URL values are runtime/deployment configuration and are not
  hardcoded.
- Raw-IP HTTP is limited to private smoke/health checks. Real user
  credential and session traffic requires trusted HTTPS; IP-first does
  not mean plaintext authentication is allowed.
- A verified domain plus HTTPS is a production-enablement prerequisite
  for Google web sign-in, not a requirement for the initial IP-only
  email/password deployment.
- Production secrets remain outside Git.
- The initial database backup requirement is a daily off-site PostgreSQL
  backup with a documented restore procedure.
- Accepted MVP recovery objectives are RPO ≤24 hours and RTO ≤8 hours;
  a real restore drill and VPS capacity proof are Phase 11 gates.
- Point-in-time recovery is evaluated later when scale and RPO/RTO
  requirements justify it.
- The shared VPS isolation and Lora safety rules in
  `docs/DEPLOYMENT.md` are mandatory.

## 8. Template Architecture

- The initial set contains approximately eight code-owned React
  templates.
- Template identity, status, free/premium classification and supported
  module metadata are database-managed.
- Renderers use a stable registry/version contract so redesigning or
  adding templates does not require rewriting the application
  architecture or breaking published invitations.
- User-authored executable template HTML/CSS/JavaScript remains out of
  scope.
