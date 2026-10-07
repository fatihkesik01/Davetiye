# Faz 7 — Gift Registry Planı

Durum: **COMPLETE (6/6) - Security and Tester/Reviewer ACCEPT; Fatih deferred clean-checkout CI to Phase 11.**
Bağımlılık: **Faz 3'ün uygulanması, tamamlanması ve bağımsız doğrulanması (tamamlandı)**

Bu belge, Fatih'in 2026-09-29'da `docs/PHASE_3_PLAN.md` için verdiği açık
talimatla aynı istisna kapsamında erken hazırlandı (bkz.
`docs/AI_WORKFLOW.md` §8'in olağan akışı). Fatih Phase 7'yi 2026-10-05'te
açıkça onayladı ve PD-04 Gift ile PD-10'u yanıtladı. Phase 7, Faz 4/5/6'dan bağımsız olarak (yalnız Faz 3'e
dayanarak) planlanmıştır; bu fazlar artık tamamlanmıştır.

Kaynak: Bu taslak `docs/ROADMAP.md` §12 (Phase 7)'deki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Creator'ın hediye item/miktar yönetimini, Guest'in kısmi rezervasyon
yapmasını, aynı browser'dan iptal edebilmesini ve Creator'ın rezervasyon
kaldırma akışını race-safe biçimde kurmak. Bu faz var, çünkü anonim miktar
mutasyonu ayrı transaction ve invitation-scoped capability güvenliği
gerektirir: Guest kalan miktarı görür, adını verip kısmi rezervasyon yapar
ve aynı browser'da iptal edebilir; diğer Guest'ler kimliği görmez.

## Kapsam

Item'lar, requested/remaining miktarlar, `GuestGiftSession`, reserve,
cancel, Creator görünümü/kaldırma, public ilerleme göstergesi.

## Kapsam Dışı

Guest account; otomatik expiry; kargo/adres; ödeme.

## Bağımlılıklar

Faz 3'ün tamamlanması; ADR-0002/0004 kabulü.

## Kabul Edilen Ürün Kararları (Fatih, 2026-10-05)

- **PD-04 Gift downgrade:** `giftRegistryEnabled` kapanınca hediye listesi
  misafirlerden gizlenir ve yeni rezervasyonlar durur. Item/rezervasyon verisi
  korunur; Creator listeyi görüp yönetebilir. Yeniden açılırsa liste public olur.
- **PD-10 identity/contact retention:** ad-soyad zorunlu; e-posta ve telefon
  ayrı ayrı opsiyonel. Guest iptali veya Creator kaldırması tüm rezervasyon
  satırını ve kimliği siler; aktif rezervasyonda kişisel veri invitation
  permanent deletion'a kadar tutulur.

Schema milestone'u (P7-M1) öncesinde açık ürün kararı kalmadı.

Fatih 2026-10-05'te **PD-17**'yi kabul etti: aktif Guest rezervasyonu olan
item silinemez; Creator önce bağlı rezervasyonları tek tek kaldırmalıdır.
Bu işlem her rezervasyonun tam kaydını ve kimliğini siler, miktarı yeniden
rezerve edilebilir hale getirir; item ancak aktif rezervasyonu kalmadığında
silinir.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü | Implementation / verification |
| ---: | --- | --- | --- | --- | --- |
| 1 | Gift/privacy contract | Faz 3 | Architect, Backend, Database, Security | Item, reservation, session, contact minimization/retention şeması; PD-10 accepted; private/public projection'lar review edildi; invitation purge integration'ı PII'yi aynı transaction'da temizler. | Complete — Database p7_m1_database; purge integration Backend p7_m1_purge; Architect p7_m1_architect review; Security p6_m2_security ACCEPT after one Medium purge finding was fixed; Tester/Reviewer p6_m3_tester_review ACCEPT. Build 0 warnings/errors; Gift domain 4/4; Architecture 79/79; PostgreSQL Gift persistence 1/1 and invitation purge 1/1; EF no pending model changes; DB count 425→425. |
| 2 | Creator item management | 1 | Backend, Frontend, UI/UX | Add/edit/delete/reorder, owner BOLA, validation, keyboard/a11y; PD-17 delete guard and collision-safe reorder. | Implemented; PostgreSQL CRUD/BOLA/reorder/delete guard, frontend panel tests, build, Unit 388/388, Architecture 79/79 and web checks passed; independent Tester/Reviewer and Security gates ACCEPT. |
| 3 | Public projection | 1, 2 | Backend, Frontend, Security | Requested/remaining quantities only; no guest identity in public projection; active-window/module/grant/entitlement gates. | Implemented; public projection omits contact and reservation identity, checks template module and publication/entitlement access; focused PostgreSQL integration passed. |
| 4 | Transaction-safe reserve | 1, 2, 3 | Backend, Database, Security | Partial quantity, required name, optional contacts, scoped session capability; no overbooking; 256-bit token/HMAC; CSRF/Origin/rate-limit. | Implemented; PostgreSQL tests cover partial reservation, concurrent overbooking, HMAC-backed cookie, CSRF 400, foreign Origin 403, and rate-limit 429. |
| 5 | Cancellation and Creator removal | 4 | Backend, Frontend, Security | Guest cancels only own session reservation; Creator owner removal; reduction survives downgrade; foreign capability rejected; all reservation PII deleted on removal. | Implemented; tests cover same-browser cancel/re-reserve, foreign capability denial, Creator removal, PII deletion and retained Creator view during downgrade. |
| 6 | Privacy/concurrency closure | 1-5 | Tester, Security, Reviewer | Conflict/token-loss/retention/accessibility tests pass; identity privacy and race/capability matrix; clean CI. | Local closure checks pass: solution build 0 warnings/errors; PostgreSQL Gift endpoints 3/3; frontend Gift component 3/3; focused test coverage includes paused gate, entitlement downgrade, identity redaction, CSRF/Origin/rate-limit and no-overbooking. Independent Tester/Reviewer and Security final ACCEPT; clean-checkout CI explicitly deferred by Fatih to Phase 11. |

### Karar Kaydı

- **PD-17 — Item silme ve aktif rezervasyonlar:** **Accepted 2026-10-05:**
  aktif Guest rezervasyonu olan item silinemez. Creator önce rezervasyonları
  ayrı ayrı kaldırır; her kaldırma tüm rezervasyon/kimlik satırını siler ve
  miktarı serbest bırakır. Bkz. `docs/PHASE_0_PLAN.md` §12 ve
  `docs/PRODUCT.md` §13.

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──┬──> M3 ──┐
            │         ├──> M4 ──> M5 ──> M6
            └─────────┘
```

M2 (Creator item yönetimi) ve M3 (public projection) M1'den sonra paralel
yürütülebilir. M4 (reserve) her ikisine bağımlıdır ve serieldir (aynı
`GuestGiftSession`/reservation tablosuna dokunur).

## Beklenen Uzman Rolleri

- **Architect:** ADR-0002/0004 sınırları, gift/privacy contract review.
- **Backend + Database:** transaction-safe reserve, capability, concurrency.
- **Security:** contact minimization, token scope, CSRF/origin/rate-limit.
- **Frontend + UI/UX:** item yönetimi, public ilerleme göstergesi, a11y.
- **Tester:** concurrent reserve, same-browser cancel, foreign token, quota
  downgrade, private contact projection testleri.
- **Reviewer:** faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture testleri ve gerçek PostgreSQL Testcontainers integration
  testleri (özellikle concurrent reserve/overbook reddi) geçer.
- Saf unit test kapsamı zorunlu değildir (Fatih'in kararı: manuel/UI testi
  tercih edilir); izole edilmesi güç, kritik mantık için (örn. hard-ceiling/
  value-type/concurrency guard'ları) hedefli unit test eklenebilir ama şart
  değildir.
- Same-browser cancel, foreign token ve quota downgrade testleri geçer.
- Private contact projection ve retention testleri geçer.
- CI clean checkout'ta bu kontrolleri tekrarlar.

## Faz Tamamlanma Kriterleri

- Overbooking and reservation identity leakage are excluded by integration tests.
- PD-10 schema/retention is implemented; cancellation/removal physically deletes the reservation PII.
- Local validation is complete; independent Tester/Reviewer and Security returned ACCEPT with no remaining blocking findings.
- Fatih deferred the clean-checkout CI repeat to Phase 11; it is tracked there rather than blocking Phase 7 closure.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; contact schema/retention, capability
scope, concurrency stratejisi ve run evidence günceller.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 7 uygulama izni değildir. Faz 7, ancak Faz 1–3
fiilen tamamlanıp bağımsız doğrulandıktan **ve** Fatih bu planı (veya
doğrulanmış önceki fazlar sonrası güncellenmiş halini) açıkça onayladıktan
sonra başlayabilir. Faz 7'nin tamamlanması, accepted guest interaction
setini tamamlar; Commerce/Admin/Privacy fazları yalnız explicit approval
ile başlar.
