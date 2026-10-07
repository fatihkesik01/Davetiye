# Faz 5 — RSVP & Attendance Insights Planı

Durum: **COMPLETE (6/6) — Fatih 2026-10-05'te Phase 4 kapanışından sonra uygulamayı açıkça onayladı; 2026-10-05'te Security ve Tester/Reviewer faz kapanışını kabul etti**
Bağımlılık: **Faz 3 ve Faz 4'ün uygulanması, tamamlanması ve bağımsız doğrulanması**

## Güncel Uygulama Durumu

P5-M1 (RSVP schema/config), P5-M2 (Creator form management), P5-M3
(anonymous submission), P5-M4 (same-browser update), and P5-M5 (Creator
results/duplicate deletion) are complete.
M1 implemented the domain/schema/migration and permanent-purge coordination;
Security and Tester/Reviewer ACCEPT after one Medium cross-invitation
question-attachment finding was fixed and reverified. M2 implemented the
Creator configuration API and accessible question-management UI, including
keyboard reorder controls and participant-count impact warnings. Fatih chose
that questions may be edited only while an invitation is Draft or Paused;
Active invitations are read-only until paused. Backend and Frontend owned M2;
Tester/Reviewer ACCEPT after one Medium participant-count-warning finding was
fixed; Security ACCEPT with no C/H/M, and one Low recommendation to add
configured Creator write rate limits in P5-M6. M3 implements public question
projection and anonymous submit with access/module/grant gates, locked quota,
rate limiting, Origin/antiforgery checks, and a scoped HttpOnly capability
cookie. Security and Tester/Reviewer ACCEPT. Approved validation bounds are
enforced server-side with strict configurable ceilings; public `answerLimits`
contains only Guest-facing values and the Creator UI consumes API-projected
`inputLimits`. Solution build, Unit 265/265, Architecture 67/67, PostgreSQL
RSVP/public integration 45/45, frontend 126/126, lint/typecheck/build, and
OpenAPI sync/check pass. Fatih selected delete-only duplicate management for
P5-M5. M4's capability-protected own-submission GET/PUT rotates the cookie
capability, revokes replayed tokens, preserves the original expiry, and does
not consume another response quota. Exact decimal lexemes remain intact across
submission, reload, and update. Security and Tester/Reviewer ACCEPT. Final
verification: solution build with 0 warnings, Unit 266/266, Architecture
67/67, PostgreSQL RSVP/public integration 46/46, frontend 135/135, lint,
typecheck, production build, and OpenAPI check pass. M5 adds account-owned,
private paged responses, answer detail, snapshot-faithful per-question
aggregates, and permanent delete-only duplicate management. Security and
Tester/Reviewer ACCEPT; one Medium historical-aggregate issue and one Low
disabled-RSVP navigation issue were fixed. M5 verification: solution build
(0 warnings), focused PostgreSQL/OpenAPI 2/2, broader RSVP/public/API contract
54/54 before the final SuperAdmin guard (which is included in the focused M5
run), frontend 142/142, lint/typecheck/build/API check. P5-M6 race, abuse, and
P5-M6 implements independently configurable Creator RSVP read/write account
limits plus read/write IP limits (fallback abuse layer), all with private 429
responses. Security ACCEPT (no C/H/M). Final verification: solution build
(0 warnings/errors), Unit 267/267, Architecture 67/67, PostgreSQL public RSVP,
Creator RSVP, API contract and Admin MFA/Google tests 80/80, focused final
rate-limit/capability/BOLA tests 3/3, frontend 142/142, lint/typecheck/build,
and generated API check. The focused coverage proves concurrent RSVP quota,
authenticated Creator B→A 404, Guest A's valid capability cannot read Guest
B's real submission or a submission on another invitation, account and IP
read/write throttling, and private 429 cache headers. Independent Tester/
Reviewer ACCEPT; Security ACCEPT; no open Critical, High, or Medium findings.
Phase 5 is complete. Phase 6 remains unapproved and must not begin until Fatih
explicitly authorizes it.

Bu belge, Fatih'in 2026-09-29'da `docs/PHASE_3_PLAN.md` için verdiği açık
talimatla aynı istisna kapsamında erken hazırlandı (bkz.
`docs/AI_WORKFLOW.md` §8'in olağan akışı). Bu plan tek başına uygulama izni
değildi. Fatih 2026-10-05'te Phase 4 tamamlandıktan sonra Phase 5'in
uygulanmasını açıkça istedi; Phase 4 kapanmıştır ve Phase 5 aktiftir.

Kaynak: Bu plan `docs/ROADMAP.md` §10 (Phase 5)'teki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Creator'ın RSVP sorularını yönetmesini, Guest'in hesapsız yanıt verip
güncelleyebilmesini ve Creator'ın private yanıtları/aggregate sonuçları
görmesini sağlamak. Bu faz var, çünkü ana guest interaction'ı, public
projection'dan ayrı purpose-scoped capability ve concurrency kurallarıyla
güvenli kurulmalıdır: Guest RSVP gönderir, aynı browser yanıtını
güncelleyebilir; Creator cevapları, katılım toplamlarını ve duplicate
kayıtları yönetir.

## Kapsam

RSVP config/soru tipleri/sıra/required, submission, `rsvp-manage`
capability, quota, sonuçlar, katılımcı aggregate'leri.

## Kapsam Dışı

Kişiye özel guest link'leri; garanti edilen cross-device dedupe; guest
account/import; her RSVP için email bildirimi.

## Bağımlılıklar

Faz 3'ün effective Active/module/entitlement gate'lerinin uygulanması.

## Kabul Edilen Ürün Kararları

- Fatih 2026-10-05'te Creator duplicate management için yalnızca silme
  aksiyonunu seçti. Ignore ve merge desteklenmez; silinen submission'ın
  yanıtları ve ona ait capability kayıtları kalıcı olarak silinir.

Fatih 2026-10-05'te RSVP bounds kararını verdi: ShortText 200, LongText 2.000,
ParticipantCount 0–20 integer, MultipleChoice selections 10, active questions
20 per invitation. Implementation also bounds question prompts to 200 chars,
option labels to 100 chars, and options per choice question to 20; unmarked
Number questions use decimal representation range. These are backend-validated
input constraints and are configurable where appropriate. The duplicate-
management behavior is delete-only per Fatih's 2026-10-05 decision.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü | Uygulama / kalite kapısı ve bulgular |
| ---: | --- | --- | --- | --- | --- |
| 1 | RSVP schema/config | Faz 3 | Backend, Database, UI/UX, Security | Form, soru/seçenek, semantic participant count ve submission ownership migration + privacy review geçer; type/order/required constraint'leri testli. | Backend + Database; Security + Tester/Reviewer ACCEPT; 1 Medium cross-invitation attachment fixed. |
| 2 | Creator form yönetimi | 1 | Backend, Frontend, UI/UX | Varsayılan soru seti, edit/delete/reorder/required; keyboard sorting testli; stats etkisi değişiklik öncesi açıklanır; BOLA testleri geçer. | Backend + Frontend; Security ACCEPT (C/H/M: 0; 1 Low hardening note for M6); Tester/Reviewer ACCEPT (1 Medium warning defect fixed). |
| 3 | Anonymous submit capability | 1, 2 | Backend, Security | Active/module/quota/rate-limit kontrolü sonrası create ve 256-bit scoped token; Secure/HttpOnly cookie; HMAC digest; CSRF + Origin kontrolü testli. | Backend + Frontend; Security ACCEPT; Tester/Reviewer ACCEPT. Approved bounds, strict configurable ceilings, Guest-only public `answerLimits`, and dynamic Creator UI verified. Solution build; Unit 265/265; Architecture 67/67; PostgreSQL RSVP/public integration 45/45; frontend 126/126 + lint/typecheck/build; OpenAPI sync/check pass. |
| 4 | Same-browser update | 3 | Backend, Frontend, Security | Cookie-capability ile yalnız kendi submission'ını okuma ve güncelleme; capability rotation/replay/scope/cookie-loss testleri geçer; update quota sayısını artırmaz; başka guest yanıtı gösterilmez; farklı cihazda duplicate kısıtı korunur. | Complete; Security ACCEPT; Tester/Reviewer ACCEPT. Solution build (0 warnings), Unit 266/266, Architecture 67/67, PostgreSQL RSVP/public integration 46/46, frontend 135/135, lint/typecheck/build/OpenAPI check pass. |
| 5 | Creator sonuçları ve duplicate yönetimi | 2, 3, 4 | Backend, Frontend, UI/UX | Private liste, aggregate'ler ve yalnız kabul edilen delete aksiyonu; Guest/Admin başka cevap/aggregate görmez; export/backlog eklenmez. | Complete; Backend + Frontend; Security ACCEPT; Tester/Reviewer ACCEPT (1 Medium historical snapshot aggregate and 1 Low disabled-results navigation finding fixed). Build 0 warnings, PostgreSQL/OpenAPI focused 2/2 + final M5 route assertions, frontend 142/142 + lint/typecheck/build/API check. |
| 6 | Race, abuse ve kapanış | 1–5 | Backend, Tester, Security, Reviewer | Concurrent quota, validation, rate-limit, a11y/E2E testleri geçer; over-quota race yok; cross-account/capability matrisi ve temiz CI. | Complete; Security ACCEPT and Tester/Reviewer ACCEPT (no C/H/M); final solution/unit/architecture/PostgreSQL/frontend/OpenAPI checks pass. Added authenticated Creator B→A 404, two-Guest and cross-invitation capability isolation, and IP write 429 coverage after independent review. No product quota changes. |

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──┬──> M3 ──> M4 ──┐
            │                 ├──> M5 ──> M6
            └─────────────────┘
```

M2 (Creator form yönetimi) ve M3 (anonymous submit) M1'den sonra dikkatli
biçimde paralel yürütülebilir; M4 M3'e serieldir (aynı capability/cookie
sözleşmesine dokunur). M5 hem M2 hem M4'e bağımlıdır.

## Beklenen Uzman Rolleri

- **Backend + Database:** schema, capability/token, concurrency-safe quota.
- **Security:** capability purpose/resource-scoping, CSRF/Origin/rate-limit,
  iki-Guest izolasyon testleri.
- **Frontend + UI/UX:** form yönetimi, keyboard sorting, sonuç ekranları,
  a11y.
- **Tester:** iki-Creator BOLA, iki-Guest capability izolasyonu, cookie
  loss, duplicate, concurrent quota.
- **Reviewer:** faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture testleri ve gerçek PostgreSQL Testcontainers integration
  testleri (özellikle concurrent quota ve iki-Creator/iki-Guest BOLA) geçer.
- Saf unit test kapsamı zorunlu değildir (Fatih'in kararı: manuel/UI testi
  tercih edilir); izole edilmesi güç, kritik mantık için (örn. hard-ceiling/
  value-type/concurrency guard'ları) hedefli unit test eklenebilir ama şart
  değildir.
- Cookie loss, duplicate submission ve capability replay testleri geçer.
- Keyboard/screen reader ile form doldurma ve sıralama testleri geçer.
- CI clean checkout'ta bu kontrolleri tekrarlar.

## Faz Tamamlanma Kriterleri

- Private response sınırları (Guest başka cevap göremez, Admin normal
  yüzeyde göremez) testlerle kanıtlanmıştır.
- Accepted duplicate management davranışı implement edilmiş ve testlidir.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer onayı
  vardır. Açık Critical/High bulgu yoktur.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; capability purpose/cookie adı/expiry,
aggregate semantics, duplicate-management kararı ve run evidence günceller.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 5 uygulama izni değildir. Faz 5, ancak Faz 1–3
fiilen tamamlanıp bağımsız doğrulandıktan **ve** Fatih bu planı (veya
doğrulanmış önceki fazlar sonrası güncellenmiş halini) açıkça onayladıktan
sonra başlayabilir. Faz 5'in tamamlanması sonraki onaylı slice'ın otomatik
başlamasına izin vermez.
