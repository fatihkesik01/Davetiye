# Faz 8 — Commerce, Plans & Transactional Email Planı

Durum: **COMPLETE (7/7)** — P8-M1 decisions and P8-M2–M7 implementation/verification are complete. Security and Tester/Reviewer accepted Phase 8 closure on 2026-10-06. Authenticated iyzico refund/dispute/chargeback source verification, real Resend/iyzico account acceptance, and clean-checkout CI repeat remain Phase 11 gates. Fatih approved Phase 8 on 2026-10-05; P8-M1 decisions and Architect/Security contract review were accepted on 2026-10-06. Fatih confirmed on 2026-10-06 that a final lost chargeback revokes the grant and stops publication while preserving data; an open dispute leaves access unchanged.
Dependencies: Phase 3 grant/resolver foundation; ADR-0004/0006; local fake-provider and official fixture contracts. Actual iyzico/Resend account setup and acceptance are deferred to Phase 11 per Fatih.

Phase 8 is explicitly authorized; only Phase 8 may be implemented under this approval. Later phases remain gated on separate approval.
Kaynak: Bu taslak `docs/ROADMAP.md` §13 (Phase 8)'deki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Individual tek seferlik hak, Organization abonelik yaşam döngüsü, iyzico
checkout/imzalı webhook/idempotent hak açma, Creator plan/ödeme UX'i ve
Resend transactional gönderimini production-shape sözleşmeyle kurmak. Bu
faz var, çünkü ücretli hak açma browser redirect'ine güvenemez; payment,
entitlement ve email yan etkileri durable ve audit edilebilir olmalıdır:
Creator paket görür/satın alır, ödeme durumunu takip eder; doğrulama/reset
ve kabul edilmiş transaction/lifecycle e-postaları ulaşır.

## Kapsam

Effective plan/grant iş kuralları, `PaymentAttempt`, checkout, webhook
inbox, atomik hak açma, abonelik, bildirim şablonları, Resend adapter/
outbox/retry.

## Kapsam Dışı

Backlog para birimleri; yeni payment provider; provider hesabı/merchant
acceptance ve kesin yasal refund/tax wording (Phase 11); production go-live.

## Bağımlılıklar

Phase 3 grant/resolver foundation; ADR-0004/0006; local fake-provider and official fixture contracts. Real iyzico/Resend account setup and provider acceptance are deferred to Phase 11 per Fatih.

## P8-M1 Accepted Product Contract

Fatih accepted the Phase 8 MVP commerce contract on 2026-10-06. PD-02/03/04
remain accepted and must not be reopened. PD-12 is accepted for this MVP
scope; exact merchant retry settings and legal refund/tax wording are Phase 11
verification gates. Full decisions are recorded in `docs/PRODUCT.md` and
`docs/PHASE_0_PLAN.md` section 12.

A provider-confirmed full refund after support review revokes the associated
Standard/Premium grant, stops its publication, and preserves invitation data.
The application does not initiate automatic or self-service refunds. A final lost chargeback has the same access effect; an open dispute does not change grant/publication state before a final provider outcome.

The notification catalog includes the existing verification/reset emails;
successful/failed Standard/Premium purchase notifications; successful/failed
Organization renewal notifications (failed-renewal notice once on the first
failed attempt per billing period); subscription cancellation confirmation;
7-day expiry reminders for canceled Organization access and Individual
publication grants; and successful invitation publication confirmation.
Per-RSVP and per-memory notifications remain excluded per `PRODUCT.md` section
26.

The commercial seed baseline matches `PRODUCT.md` section 19. P8-M1 is complete after independent Architect and Security ACCEPT on
2026-10-06. P8-M2 one-time checkout is the next implementation milestone.

### P8-M2/M4/M5 technical contract gates

- M2/M4 initially implement Individual Standard/Premium one-time purchases
  only. They must not add partial Organization-subscription columns to
  `PaymentAttempt` or use `AccountPlanGrant.RevokedAt` to represent both
  cancellation and paid-through expiry.
- Checkout is request-idempotent: an idempotency key is scoped to the owning
  account and purchase; replaying it with the same parameters returns the
  same attempt, while reusing it with different parameters is rejected. A
  same-key replay uses the immutable attempt snapshot even if its plan is
  later deactivated from the current catalog.
  new attempt for the same purchase is blocked while the
  prior provider state is pending/unknown; retry is allowed after provider
  confirmation of failure/cancellation. A redirect never grants access.
  Provider checkout URLs must match the gateway's trusted origin; provider
  timeout/cancellation is durably recorded as `Unknown` before returning.
- M4 creates a one-time grant only after server-verified successful payment.
  A provider-confirmed full refund after support review transitions payment,
  revokes the grant and stops publication atomically, preserving invitation
  data. A final lost chargeback has the same effect; an open dispute does not change access. Refund/chargeback outcomes must be deduplicated and order-safe.
- M5 owns a separate Organization subscription aggregate with account/plan,
  provider subscription identity, billing-cycle identity, current
  `PaidThroughAt`, and `CancelAtPeriodEnd` state. A successful verified
  renewal advances `PaidThroughAt`; cancellation stops future renewal but
  preserves access until that instant. The entitlement/public resolver gates
  synchronously at `PaidThroughAt`, independent of a delayed background job,
  then preserves invitation data. Retry schedule details are provider
  configured and verified in Phase 11.
- A failed-renewal notification is emitted once for the first failed attempt
  per billing cycle (idempotency key includes the subscription and cycle), not
  once for every automatic retry.

### P8-M1 seed reconciliation (read-only, 2026-10-05)

`PlanCatalogInitializer` currently matches the `PRODUCT.md` section 19 starter
TRY prices, billing kinds, and entitlement values for Free, Standard, Premium,
and Organization. The initializer inserts missing rows without overwriting
operator-edited DB values. This confirms the current seed baseline; it does
not authorize a schema/seed change. MVP price display is accepted above;
tax-inclusion wording remains a Phase 11 verification gate.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Commerce decision/contract | Phase 3 + user decisions | Fatih, Architect, Backend, Security, UI/UX | Accepted MVP contract is recorded; Architect/Security review passes; grant/renewal/cancel/refund/notification behavior and seed values reconcile with PRODUCT.md; legal/provider gates remain assigned to Phase 11. |
| 2 | `PaymentAttempt`/checkout | 1 | Backend, Database, Frontend, Security | Individual Standard/Premium one-time only; authenticated catalog shows server-configured amount/currency/period; checkout uses server plan/amount/TRY, immutable attempt/reference, purchase-scoped idempotency, no parallel pending/unknown attempts for same purchase, trusted provider origin and durable Unknown after timeout/cancel; retry only after provider-confirmed terminal state; browser input authoritative değildir; secret/card data tutulmaz; negative contract tests pass. |
| 3 | Signed webhook inbox | 2 | Backend, Database, Security | For the documented Checkout Form HPP `CHECKOUT_FORM_AUTH` event, verify the official V3 HMAC preimage, bound request bytes, persist a minimized normalized event durably, derive the unique event key from the signed tuple, retain unsigned provider reference/time only as explicitly untrusted metadata, and acknowledge only after durable acceptance or confirmed duplicate. No raw body, token, or signature is persisted. Refund, dispute, and chargeback event ingestion requires a separate verified source/operational decision; it is not implemented by this HPP adapter. Merchant-side signature enablement and provider behavior acceptance remain Phase 11 gates. |
| 4 | Atomik idempotent hak açma | 2, 3 | Backend, Database, Security | Merchant/reference/account/plan/amount/currency/state reconcile edilir; payment transition and grant creation/revocation occur atomically; duplicate/retry/out-of-order events do not duplicate or regress state; confirmed full refund or final lost chargeback revokes the grant and stops publication while preserving data; open chargeback does not change access; `AccountId` narrow contract; redirect hak açmaz. |
| 5 | Abonelik ve Creator UX | 1–4 | Backend, Frontend, UI/UX | Payments-owned subscription state keeps provider identity, billing-cycle identity, paid-through and cancel-at-period-end; resolver gates synchronously at paid-through; retries/notifications are deduplicated per cycle; cancellation, renewal, expiry and data-preservation tests plus BOLA/accessibility pass. |
| 6 | Resend/outbox notifications | 1 | Backend, Security | Resend adapter and templates for accepted verification/reset/purchase/renewal/cancellation/publication/expiry messages; failed-renewal email once per billing cycle; outbox retry and registration compensation; trusted base URLs; no PII/secret logs. Email verification/reset tokens must not be stored as ordinary plaintext outbox payloads; use protected, narrowly accessible, short-lived payload handling and keep account-enumeration responses uniform. Actual Resend account/deliverability checks are Phase 11. |
| 7 | Local integration closure | 1–6 | Tester, Security, Reviewer, Architect | Official signed-payload fixture tests, fake gateway fail-closed, duplicate/replay/out-of-order, dispute-open/final-won/final-lost chargeback, full-refund, and provider-outage simulation; commerce/email suites green. Actual iyzico sandbox and Resend account acceptance are deferred to Phase 11 per Fatih. |

## P8-M2 Implementation Status (2026-10-06)

P8-M2 implementation and independent Architect, Security, and Reviewer checks
are complete. The authenticated Standard/Premium catalog and checkout use
server prices, individual-account and invitation ownership checks, scoped
idempotency, a single pending/unknown DB constraint, trusted gateway origins,
and explicit terminal/uncertain client states. Timeout/cancellation persists
`Unknown`; the browser never grants access. The additive migration and API
contract are in place. Build, unit, architecture, OpenAPI/client, and web
checks pass. PostgreSQL HTTP integration tests cover catalog, authorization,
retries, race conditions, and cancellation; their full Testcontainers run is
recorded under P8-M7 below. Real iyzico and Resend account acceptance remains
deferred to Phase 11.

## P8-M3 Implementation Status (2026-10-06)

The documented Checkout Form HPP payment-notification inbox slice is implemented
and independently accepted by Security and Reviewer. It verifies the official
`X-IYZ-SIGNATURE-V3` HMAC preimage for `CHECKOUT_FORM_AUTH`, enforces the
configured streaming request-body limit, and writes a minimized envelope via
the provider-neutral Integration Foundation inbox port. The idempotency key is
derived from signed fields. `iyziReferenceCode` and `iyziEventTime` are stored
only as explicitly untrusted metadata because HPP V3 does not sign them; they
must never authorize, order, or transition payment state. The token, signature,
and raw body are discarded. No entitlement transition occurs in the webhook
acknowledgement path.

Unit tests (11/11), TestServer endpoint tests (8/8), ArchitectureTests (79/79),
solution build (0 warnings/errors), and `npm run api:check` pass. The PostgreSQL
unique-index writer test compiles but could not execute because Docker Desktop's
Linux engine is unavailable. Official iyzico documentation describes payment
events but no generic full-refund, dispute-open, or chargeback-outcome webhook
contract. The accepted business outcomes are recorded above and in
`PRODUCT.md`/PD-12; their authenticated source and merchant behavior remain a
Phase 11 provider-verification gate.
See [iyzico Webhook](https://docs.iyzico.com/en/advanced/webhook) and
[Refund & Cancel](https://docs.iyzico.com/en/advanced/refund-and-cancel).

## P8-M4 Implementation Status (2026-10-06)

The local atomic settlement slice is implemented and independently accepted
by Security and Tester/Reviewer. Only provider-filtered documented HPP events
reach the payment processor. The signed callback tuple is reconciled with the
stored checkout identity, then authenticated Checkout Form Retrieve
`POST /payment/iyzipos/checkoutform/auth/ecom/detail` uses the stored checkout
token and attempt reference before state transition. The response HMAC is
verified over the documented Checkout Form fields, including `paymentStatus`
and token; the returned token, payment ID, reference, currency, price, and
paid price must match the immutable attempt. A grant is created only for
`status=success`, `paymentStatus=SUCCESS`, and `fraudStatus=1`. The API response
is credential-authenticated over HTTPS; top-level `status` and `fraudStatus`
are not in its HMAC. Provider sandbox and merchant behavior acceptance remain
Phase 11 gates.

Successful attempt state, provider payment ID, linked grant ID, reserved
individual grant, one stable purchase-email outbox row, and inbox completion
commit in one database transaction. Confirmed terminal failure records a
failed attempt and one stable failure-email outbox row without a grant.
Provider outage and nonterminal/review results leave the attempt unchanged
and schedule bounded inbox retry. Additive migrations add unique nullable
payment/grant identities, reversal state, and chargeback-resolution integrity
checks.

The internal provider-neutral reversal operation is callable only by a future
trusted source after it verifies a terminal outcome; it is not an API and no
refund/dispute/chargeback ingress or support workflow is included. Open
disputes do not change access. Final won chargebacks are persisted as a
tombstone without changing access, so a late final-lost result cannot revoke
the grant; a later confirmed full refund can still revoke it. Confirmed full
refunds and final lost chargebacks atomically mark the attempt Reversed,
revoke only its linked individual grant, archive a matching current
publication window, and move that invitation to Draft while preserving its
data. Duplicate same outcomes are idempotent; conflicting terminal outcomes
cannot replace the stored outcome. A NotFound result is retryable: a trusted source must retain
and retry a reversal that arrives before payment settlement. The applied
timestamp comes from local IClock on retry, never the unsigned provider event
timestamp. Refund/chargeback source and merchant acceptance remain Phase 11
gates.

Verification: solution build (0 warnings/errors), focused payment tests
28/28, UnitTests 449/449, ArchitectureTests 79/79, and EF
`has-pending-model-changes` pass. PostgreSQL integration scenarios compile and
cover atomic success/replay, confirmed failure, outage retry without attempt
mutation, both reversal outcomes, open-dispute/final-won behavior,
won-before-stale-loss, retry-before-settlement, duplicate/conflicting
outcomes, and delayed HPP replay after reversal. At M4 implementation time,
Testcontainers could not connect to Docker at `npipe://./pipe/docker_engine`;
P8-M7 later executed the full integration suite against PostgreSQL and records
the results below. No push, deployment, or live provider request was made.

## P8-M5 Implementation Status (2026-10-06)

Organization subscriptions now have a Payments-owned aggregate and billing-cycle ledger, additive migrations, verified activation/renewal application ports, per-account locking, unique provider/cycle identities, and transactional renewal/cancellation notifications. Creator cancellation is owner-scoped and antiforgery-protected; the Creator plan-and-payment screen shows the server-derived subscription state, amount, and paid-through time. Organization grants are shared across invitations; active access is gated synchronously at `PaidThroughAtUtc`, while lifecycle reconciliation expires invitations and archives windows without deleting content. Renewal and cancellation behavior, one-cycle notification deduplication, and permitted in-flight settlement are covered by authored PostgreSQL integration scenarios.

The 7-day canceled-subscription reminder runs through the durable email outbox. A persisted queue marker and stable paid-through-boundary message ID prevent duplicates; a verified in-flight renewal clears the marker for the extended period. Failed enqueue attempts receive a bounded retry delay greater than the polling interval, and fresh due reminders sort ahead of retries. The late-cancellation path queues on the next worker poll when access is already within the seven-day window.

The Creator API response is mapped to the minimal OpenAPI/client fields and omits account, plan, and provider identifiers. Independent Security and combined Tester/Reviewer checks accepted the implementation. Local verification passed at M5 implementation time: solution build (0 warnings/errors), UnitTests (463/463), ArchitectureTests (79/79), subscription endpoint tests (4/4), frontend tests (206/206), lint, typecheck, production build, `api:check`, and EF `has-pending-model-changes`. The new PostgreSQL subscription/reminder integration scenarios were later executed successfully in P8-M7. Actual Resend delivery and provider acceptance remain Phase 11 gates.

## P8-M6 Implementation Status (2026-10-06)

The durable transactional-email slice is implemented and independently
accepted by Tester/Reviewer and Security. Auth verification/reset payloads are
Data Protection-encrypted in the shared outbox and expire no later than their
Identity tokens; new bearer links use URL fragments, which the SPA consumes and
clears before its API call. Query-string bearer links are rejected and
scrubbed. Registration queues confirmation inside the Identity/account
transaction so enqueue failure rolls the account back; password-reset queue
failures keep the uniform public response. Templates cover verification,
password reset, purchase/renewal success and failure, cancellation, separate
Individual-publication and canceled-Organization-access 7-day reminders, and
successful publication.

The Resend adapter uses a stable outbox idempotency key, disables redirects,
classifies provider failures, and logs no recipient or message content. The
worker retries transient failures with bounded backoff and dead-letters
permanent, expired, corrupt, or over-age work before the provider's 24-hour
idempotency retention. Password-reset requests retain the per-IP limit and add
a fixed-size, process-local HMAC destination limiter; login failure responses
do not distinguish unknown, unconfirmed, wrong-password, or locked accounts.
The limiter is not shared across replicas and resets on process restart; Phase
11 production topology must account for this boundary. Actual Resend account
and delivery acceptance remain deferred to Phase 11.

At M6 implementation time, auth registration-compensation, production outbox
persistence, and login lockout integration assertions had not run because the
Docker engine was unavailable. P8-M7 later executed all integration tests and
records the verification below. No provider account, push, VPS connection, or
deployment was used.

## P8-M7 Local Integration Closure (2026-10-06)

Docker Desktop's Linux engine was started locally for this verification. The
complete `Davetiye.IntegrationTests` project passed against PostgreSQL 18 via
Testcontainers: **463/463**. This executed the authored M2–M6 database
assertions and migrations, including checkout/payment, inbox/outbox,
refund/chargeback state, Organization subscription renewal/cancellation,
expiry reminders, auth registration compensation, protected outbox persistence,
and login/session scenarios. The fixture disables per-database Npgsql
pooling so the full run stays within PostgreSQL connection limits; test
harnesses use the actual generated container password.

The run exposed and fixed an EF state-classification bug for newly added
Organization billing cycles, stale auth-link test parsers after the URL
fragment contract, incomplete publication test setup, and malformed request
handling that returned 500 instead of a safe client error. Malformed requests
now retain valid 4xx status codes such as 413 without exposing parser details.
Final verification: solution build (0 warnings/errors), UnitTests (465/465),
ArchitectureTests (79/79), IntegrationTests (463/463), EF
`has-pending-model-changes` (no changes), focused malformed/oversize endpoint
tests (3/3), and request-error handler tests (2/2). Security and
Tester/Reviewer returned ACCEPT; no open Critical, High, or Medium finding
remains. Other preserved 4xx ProblemDetails responses use a generic fixed
message and the `about:blank` type.

Actual iyzico sandbox/account acceptance, authenticated refund/dispute/
chargeback source verification, real Resend delivery, and the clean-checkout
CI repeat remain deferred to Phase 11 per Fatih. No push, VPS connection,
deployment, or live-provider operation was performed.

## Milestone Bağımlılık Grafiği

```text
M1 ──┬──> M2 ──> M3 ──> M4 ──┬──> M5 ──┐
     └──> M6 ─────────────────┘         ├──> M7
                                         │
```

M2 (checkout) ve M6 (Resend/outbox) M1'den sonra farklı yüzeyleri
etkilediği için paralel yürütülebilir. M3 ve M4, M2'nin sözleşmesinden
sonra sırayla ilerler (aynı webhook/grant transaction'ına dokunur). M5,
M4'e bağımlıdır.

## Beklenen Uzman Rolleri

- **Architect:** ADR-0004/0006 sınırları, commerce contract review.
- **Backend + Database:** payment attempt, webhook inbox, atomik grant,
  outbox.
- **Security:** signature/replay doğrulama, secret/log redaction, fail-
  closed fake gateway.
- **Frontend + UI/UX:** checkout ve plan/ödeme UX'i, accessibility.
- **Tester:** resmi sandbox fixture'ları, duplicate/out-of-order, amount/
  currency mismatch, provider outage/retry, email bounce/failure.
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
- Resmi iyzico sandbox fixture'larıyla signature/replay/duplicate/out-of-
  order webhook testleri geçer.
- Amount/currency/account mismatch ve provider outage/retry testleri geçer.
- Resend test ortamında email bounce/failure/retry testleri geçer.
- `FakePaymentGateway`'in production'da startup validation ile reddedildiği
  test edilir.
- CI clean checkout'ta bu kontrolleri tekrarlar.

## Faz Tamamlanma Kriterleri

- Sandbox ve fake provider yolları doğrulanmıştır.
- "Bir ödeme → bir hak açma" invariant'ı testlerle kanıtlanmıştır.
- Notification failure güvenli (retry/compensation'lı) şekilde ele
  alınmıştır.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer,
  Architect onayı vardır. Açık Critical/High bulgu yoktur.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; provider/API version, resmi fixture
referansları, cevaplanan kararlar, notification kataloğu ve run evidence
(secret hariç) günceller.

## Sonraki Faz Planlama Kapısı

Phase 8 is explicitly approved. Completing it does not authorize Phase 9;
obtain Fatih's explicit approval before starting Phase 9. Phase 8 makes paid
entitlements and Admin commerce visibility possible; production merchant
activation remains gated to Phase 11.


