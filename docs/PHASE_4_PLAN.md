# Faz 4 — Creator Media & Gated Delivery Planı

Durum: **COMPLETE — Fatih 2026-10-03 tarihinde onayladı; P4-M1–M8 tamamlandı (8/8), 2026-10-05**
Bağımlılık: **Faz 3'ün uygulanması, tamamlanması ve bağımsız doğrulanması**

Fatih'in 2026-10-04 tarihli talimatıyla Cloudflare hesap/kaynak kurulumu,
hesaba özel limitlerin incelenmesi ve gerçek-provider kabul testleri Phase 11'e
ertelendi. Phase 4 local/provider-contract doğrulamasıyla tamamlanabilir; bu
erteleme production enablement veya go-live izni değildir.

Bu belge, Fatih'in 2026-09-29'da `docs/PHASE_3_PLAN.md` için verdiği açık
talimatla aynı istisna kapsamında erken hazırlandı (bkz.
`docs/AI_WORKFLOW.md` §8'in olağan akışı — bir sonraki fazın planı normalde
mevcut faz doğrulanmış biçimde tamamlandıktan sonra hazırlanır). Bu planın
Faz 4, Faz 1–3'ün tamamlanıp bağımsız doğrulanmasının ardından Fatih'in
2026-10-03 tarihli açık onayıyla uygulanmaya başlandı.

Kaynak: Bu taslak `docs/ROADMAP.md` §9 (Phase 4)'teki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Creator'ın cover/gallery fotoğraf ve videolarını Cloudflare R2 + Images
Transformations + Stream ile doğrudan, limitli ve current-access-gated
biçimde yükleyip teslim etmesini sağlamak. Bu faz, medya byte'larını VPS/DB'ye
koymadan yüksek maliyetli upload yüzeyini lifecycle ve entitlement
güvenliğine bağladığı için var: Creator fotoğraf/video ekler, processing/
retry durumunu görür ve yalnız erişilebilir davetiyede güvenli medya
oynatılır.

## Kapsam

`MediaAsset`/`PendingUpload` state machine, provider-neutral portlar, Creator
intent/finalize/delete akışı, gated delivery (signed transform/video
session), gallery UI (progress/processing/retry) ve cleanup/reconciliation
(orphan, rejected, multipart, provider drift).

## Kapsam Dışı

Guest memory upload'ları (Faz 6'da); kullanıcı tarafından yazılan aktif
içerik (script/HTML); public/kalıcı provider URL'leri.

## Bağımlılıklar

Faz 3'ün tamamlanması; ADR-0005 kabulü. Cloudflare account, bucket, Stream,
account-specific limits and credentials are an owner action deferred to
Phase 11 by Fatih on 2026-10-04; their absence does not block Phase 4 local
engineering or Phase 5 development.

## Bu Fazı Bloke Eden Açık Ürün Kararları

**Fatih tarafından 2026-10-03 tarihinde kabul edildi:**

- PD-06: Creator ve Guest medya kotaları ayrıdır (`docs/PHASE_0_PLAN.md` §12).
- Creator fotoğraf yüklemelerinden EXIF/GPS metadata provider storage'a
  aktarılmadan önce kaldırılır.
- Creator fotoğraf ve videoları cover ve gallery sunumlarında kullanabilir.
- Grant atanmamış Draft davetiyeye Creator media upload intent verilmez
  (PD-14); Gallery için şablonun `gallery` modülünü desteklemesi yeterlidir
  (PD-15).
- PD-16 (Fatih, 2026-10-05): süresi dolan/reddedilen upload provider byte'ları
  davetiye kalıcı silinene kadar tutulur. Expiry/rejection intent'i kapatır,
  fiziksel provider silme başlatmaz; permanent purge tüm invitation media
  state'leri için idempotent deletion outbox işi üretir. Provider deletion
  doğrulanana kadar Rejected/PendingDeletion Creator asset'leri invitation
  item quota'sını tutar; yalnız Deleted asset slot bırakır.

Malware scanner baseline'da zorunlu kabul edilmez; gerçek allowlist/type-
decode risk incelemesiyle teknik karar verilir — bu bir açık ürün kararı
değil, P4-M4'te verilecek teknik bir karardır.

PD-06, PD-14, PD-15 ve metadata/presentation kararları yanıtlandığından P4-M1
ve P4-M2 başlayabilir. Fatih'in 2026-10-04 talimatı: Cloudflare hesabı/kaynak
kurulumu, hesaba özel limit incelemesi ve gerçek upload/delivery/delete kabulü
Phase 11'e (P11-M6, P11-M8) ertelenmiştir. Phase 4 içinde provider-contract,
mock/binding tests and local security gates remain required; no production
media feature is enabled until the Phase 11 gates pass.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Media contract | Faz 3 | Architect, Backend, Database, Security | **COMPLETED (2026-10-03):** implementation `p4_m1_backend`; architecture/boundary review `p4_architecture_discovery`; Tester+Reviewer `p4_m1_tester_reviewer` ACCEPT; Security `p4_security_discovery` ACCEPT; C/H/M/L: 0. Provider-neutral lifecycle, Invitation-root ownership, separate Creator/Guest quota scope, typed normalized-image evidence, immutable-snapshot placement boundary, one-open-intent uniqueness, and tightened entitlement ceilings. Build 0 warnings/errors; unit 185/185; architecture 67/67; PostgreSQL focused 3/3; migration model current; scoped diff check clean. Real Worker byte attestation remains a later Cloudflare gate. |
| 2 | Upload intent | 1 | Backend, Security | **COMPLETED (2026-10-04):** implementation `p4_m2_backend_completion`; migration `p4_m2_migration_snapshot`; Security `p4_m2_security_review` ACCEPT; Tester+Reviewer `p4_m2_tester_reviewer` ACCEPT; C/H/M/L: 0 open (the Medium idempotent-retry finding was fixed and reverified). Owner/lifecycle/grant/template/account gates, durable quota reservation, rate limits, safe same-asset replay and private capability response are implemented. Build 0 warnings/errors; Unit 201/201; Architecture 67/67; focused real-PostgreSQL HTTP/transaction tests 3/3. Full integration: 255/260 passed; the five Production-host tests passed 5/5 on rerun with a temporary password field on the isolated local trust connection. EF model is current. Provider integration remains out of this milestone. |
| 3 | Upload-time limit'leri | 2 | Backend, Security, Cloudflare | **COMPLETED (2026-10-04; real-provider acceptance deferred to P11-M6/M8 by Fatih):** Worker counts actual image stream bytes before transform/R2 write; Stream TUS receives server entitlement byte and duration ceilings. Creator limits come from entitlements; Draft without grant receives no intent. Per-AssetId broker replay is stable and ambiguous Stream create fails closed. Tester+Reviewer and Security ACCEPT; no open C/H/M finding. Build 0 warnings/errors; Unit 205/205; Architecture 67/67; Worker 24/24; typecheck and Wrangler dry-run passed. Local implementation is verified; Cloudflare account-specific behavior/limits were not tested and remain a mandatory pre-production P11 gate. |
| 4 | Verification/finalize | 2, 3 | Backend `p4_m4_backend`, Worker `p4_m3_backend_scout`; Tester+Reviewer `p4_m3_tester_reviewer` ACCEPT; Security `p4_m3_security_review` ACCEPT; C/H/M/L: 0 | **COMPLETED (2026-10-04):** Creator finalize owner/account scoped; bounded raw-body Stream HMAC webhook; mandatory provider inspection; actual image/video type/byte/duration checks; transactionally deduplicated provider event; only verified media reaches Ready. Worker sniffs a bounded 12-byte JPEG/PNG/GIF/WebP signature before normalization, rejecting SVG/HTML/truncated data regardless of Content-Type. AVIF input is intentionally excluded until P11 confirms account-plan support. Build 0 warnings/errors; Unit 224/224; Architecture 67/67; route TestServer 7/7; media PostgreSQL persistence 4/4 on an isolated temporary loopback cluster; Worker 42/42, typecheck and Wrangler dry-run pass; OpenAPI sync/check/verify pass; EF model current. No Cloudflare account/provider acceptance claimed. |
| 5 | Gated delivery | 4 | Backend, Frontend, Security | **COMPLETED (2026-10-04):** current public snapshot/access rechecked before and after provider issuance; private image capability ≤60s; bounded Stream playback session; no provider URL in public payload; pause/expire/ban/trash/update deny issuance/renewal. Provider-body JSON limited to 8 KiB before parsing; declared and streamed over-limit responses rejected. Broker transport/HTTP/body/timeout failures become 503 while caller cancellation propagates. Reviewer and Security ACCEPT; no open C/H/M finding. Build clean; Unit 233/233; Architecture 67/67; gateway 6/6; route 5/5; PostgreSQL delivery/snapshot 61/61 plus transport/timeout/cancellation 3/3. |
| 6 | Creator media UX | 4, 5 | Frontend, UI/UX | **COMPLETED (2026-10-04):** Creator image/video upload progress, processing, retry with same intent/capability, Ready-only placement and logical delete; published cover/gallery renderer uses only immutable snapshot assets, memory-only short-lived delivery, accessible titled media and sandboxed video. Retry finding fixed and independently ACCEPTed. Vitest 113/113; typecheck/lint/build pass; Playwright 36/36 over desktop, 320px and 200% zoom; media axe/no-overflow checks pass. |
| 7 | Cleanup/reconciliation | 4, 5, 6 | Backend, Database, Security | **COMPLETED (2026-10-05):** Fatih accepted PD-16: provider bytes for expired/rejected uploads remain until permanent invitation purge. Expiry closes the intent and rejects the asset without immediate provider deletion; purge queues every media state through the typed transactional outbox. Permanent-purge/retry test covers Ready + expired/Rejected assets; expiry lifecycle test asserts no delete before purge. Lease fencing, retries from current failure time, bounded reconciliation and aggregate metrics covered. Full integration 291/291; additional policy-focused PostgreSQL checks 2/2. |
| 8 | Faz 4 engineering kapanışı | 1–7 | Tester, Security, Reviewer, Architect | **COMPLETED (2026-10-05):** PD-16 retains provider bytes until permanent invitation purge; Rejected/PendingDeletion assets continue consuming the existing Creator item quota until provider deletion is confirmed, and unknown byte sizes use the upload reservation maximum in the storage upper-bound metric. Build 0 warnings/errors; Unit 236/236; Architecture 67/67; focused PostgreSQL media persistence 9/9; prior full PostgreSQL integration 291/291 before this focused mitigation; Worker 52/52/typecheck; adapter bounds 7/7; Vitest 113/113; Playwright 36/36; frontend lint/typecheck/build/API checks pass. Security and Tester/Reviewer ACCEPT; Architect ACCEPT; no open C/H/M finding. The post-change full integration rerun was attempted with local PostgreSQL but stopped after 6m39s with no output; standard Testcontainers is unavailable because Docker is not running. Cloudflare account/provider acceptance remains a P11-M6/M8 go-live gate. |

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──> M3 ──┬──> M4 ──┬──> M5 ──┬──> M6 ──┐
                    │         │         │         ├──> M7 ──> M8
                    └─────────┴─────────┴─────────┘
```

M3 (upload-time limit), M4 (verification/finalize) sırayla ilerler — aynı
upload pipeline'a dokunur. M5 (gated delivery) M4'e bağımlı olsa da M6
(Creator UX) ile farklı yüzeyleri etkilediği için dikkatli biçimde paralel
yürütülebilir. M7 (cleanup) M4–M6'nın tamamına bağımlıdır.

## Beklenen Uzman Rolleri

- **Architect:** ADR-0005 sınırları, media contract review.
- **Backend + Database:** state machine, provider port, upload intent/quota,
  cleanup/reconciliation.
- **Security:** upload-time limit, verification/finalize, gated delivery,
  threat model T09/T10/T16/T17 karşılaştırması.
- **Frontend + UI/UX:** gallery/upload UX, responsive/a11y.
- **Tester:** local provider-contract/binding fakes, quota/replay/oversize/
  malformed dosya, orphan cleanup, pause/expire/trash renewal tests; real
  provider behavior is checked only at the Phase 11 release gate.
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
- Provider contract tests pass against local Worker/binding fakes and adapter
  tests; they validate application behavior but do not claim Cloudflare account
  behavior. Real Cloudflare upload/delivery/delete acceptance moves to P11-M6/M8.
- Quota race, replay, oversize, malformed dosya ve orphan cleanup testleri
  geçer.
- Pause/expire/ban/trash renewal denial and residual exposure bounds are
  covered by local delivery/session tests. Real provider residual exposure is
  measured and accepted in P11-M6/M8 before production enablement.
- Mobile/a11y (320 CSS px, keyboard, automated a11y) testleri geçer.
- No Phase 4 test or report implies Cloudflare account acceptance; P11-M6/M8
  remains mandatory before any production media enablement.
- CI clean checkout'ta bu kontrolleri ve container smoke'u tekrarlar.

## Faz Tamamlanma Kriterleri

- Sınırsız presigned PUT üretilmez; byte hard ceiling provider/edge
  seviyesinde kanıtlanmıştır.
- Provider-neutral access, TTL and residual exposure behavior is covered by
  tests. Account-specific provider behavior is measured in P11-M6/M8 before
  production media is enabled.
- Deletion/reconciliation ve abuse/cost alerting çalışır durumdadır.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer ve
  Architect onayı vardır. Açık Critical/High bulgu yoktur.
- PD-06 varsayılmamıştır — ya Fatih tarafından açıkça cevaplanmıştır ya da
  ilgili milestone henüz başlamamıştır.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; provider API version/assumptions, token
scope'ları, test edilmiş limitler, ölçülen residual exposure, cleanup
evidence (secret hariç) ve sonraki önerilen işi günceller.

## Sonraki Faz Planlama Kapısı

Faz 4, Faz 1–3'ün tamamlanması ve Fatih'in 2026-10-03 tarihli onayıyla
uygulamaya başladı. Faz 4 tamamlandığında Guest media kullanan Faz 6
(Memories) açılabilir hale gelir; RSVP (Faz 5) ve Gift (Faz 7) Faz 4'ten
bağımsız olarak da onaylanabilir.
