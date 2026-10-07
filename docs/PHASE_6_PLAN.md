# Faz 6 — Memories & Guest Media Planı

Durum: **COMPLETE (6/6) — Fatih 2026-10-05'te onayladı; P6-M1–M6 tamamlandı. Security ve Tester/Reviewer faz kapanışını ACCEPT etti. Gerçek Cloudflare-provider kabulü Phase 11'e ertelendi.**
Bağımlılık: **Faz 3 ve Faz 4'ün uygulanması, tamamlanması ve bağımsız doğrulanması (tamamlandı); Faz 5 de tamamlandı**

Bu belge, Fatih'in 2026-09-29'da `docs/PHASE_3_PLAN.md` için verdiği açık
talimatla aynı istisna kapsamında erken hazırlandı (bkz.
`docs/AI_WORKFLOW.md` §8'in olağan akışı). Phase 6, önkoşullar tamamlanıp
Fatih tarafından 2026-10-05'te ayrıca onaylandıktan sonra yürütüldü ve
bağımsız kalite kapılarından geçerek tamamlandı.

Kaynak: Bu taslak `docs/ROADMAP.md` §11 (Phase 6)'daki daha önce hazırlanmış
yüksek seviyeli plan esas alınarak, `docs/PHASE_TEMPLATE.md` biçimine
dönüştürülmüştür. İçerik ROADMAP'tekiyle aynıdır; roadmap ayrıntıyı
kopyalamaz kuralına uygun olarak, bu iki belge zamanla birbirinden
sapmamalıdır — biri güncellenirse diğeri de güncellenmelidir.

## Amaç

Creator-only/Public memory visibility, text/emoji/opsiyonel ad, guest
fotoğraf/video upload/finalize ve Creator hide/delete davranışını kurmak.
Bu faz var, çünkü Memories anonim içerik ve provider maliyet/upload
riskini birlikte taşır; Faz 4'ün Creator media temeli üzerinde ayrı bir
capability modeli ister: Guest anı ve medya bırakır, processing durumunu
görür; Creator gönderileri yönetir; yalnız izin verilen Ready içerik public
listede görünür.

## Kapsam

Memory config/submission/projection, kısa ömürlü pending upload
capability, finalize, visibility (Creator-only/Public), hide/delete,
quota/rate limit.

## Kapsam Dışı

Guest'in finalize sonrası edit/delete'i; zorunlu guest identity; kapsam
dışı moderation/CMS akışları.

## Bağımlılıklar

Faz 3 ve Faz 4'ün tamamlanması; ADR-0002/0005 kabulü.

## Bu Fazı Bloke Eden Açık Ürün Kararları

`[DECISION REQUIRED — FATIH]`:

- PD-04 (Boolean entitlement downgrade'inin mevcut memories/gift/premium
  içeriğe etkisi — tam metin `docs/PHASE_0_PLAN.md` §12'de).
- PD-06 (Creator/Guest media kota paylaşımı; Faz 4'te kapanmadıysa burada
  kapanmalıdır).
- `docs/PRODUCT.md`'teki "Public ise otomatik görünebilir" ifadesinin MVP
  exact davranışı.
- EXIF/GPS privacy kararı, Faz 4'te kapanmadıysa burada kapanır.

Visibility/mod kararı, ilgili UI contract'ı (P6-M4) yazılmadan önce
cevaplanmalıdır.

### Kabul edilen kararlar (Fatih, 2026-10-05)

- **PD-04 Memories alt vakası:** `memoriesEnabled` kapanırsa mevcut anılar
  public'ten gizlenir ve saklanır; silinmez. Creator görür ve silebilir; yeni
  anı kabul edilmez.
- **Varsayılan görünürlük:** Sadece Creator. Creator Public seçerse
  metin/emoji hemen görünür; fotoğraf/video yalnız Ready olunca görünür.
- **Public → Sadece Creator:** mevcut bütün anılar geriye dönük gizlenir;
  veri değişmez. Tekrar Public yapılırsa geri görünür.
- **Modül açılışı:** davetiye başına Creator açar (varsayılan kapalı).
  Misafirler yalnız effective-Active davetiyede anı bırakabilir; Scheduled'da
  bırakamaz.
- **Creator moderasyonu (Fatih "sen karar ver" dedi; Orchestrator, 2026-10-05):**
  yalnız gizle ve sil — PRODUCT §12 başka eylem tanımlamaz; gizlenen anı geri
  açılmaz (Hidden terminal; Creator silebilir). Creator listesi yalnız
  Published ve Hidden anıları gösterir; PendingMedia/Abandoned görünmez.
  Bunlar ürün metninin dar okumasıdır; geri açma veya bekleyen anı görünümü
  istenirse ayrıca karar gerekir.
- **Ajan kararları (Orchestrator onayı, Fatih "sen karar ver" dedi):** Creator
  ayarı Active iken de değişebilir; Creator planında `memoriesEnabled` olmasa da
  modülü açabilir (misafir hak gelene kadar 404, veri korunur); satır sonu yalnız
  metinde serbest; Hidden ve PendingMedia anı üst sınıra sayılır, Abandoned
  sayılmaz; anı başına üst sınır 500 ve rate-limit değerleri mühendislik
  varsayılanıdır (ürün onaylı sayı değil); şablon kataloğunda `memories` anahtarı
  yoktur, kapı yalnız entitlement + Creator `IsEnabled`.
- **Durum kaydı:** P6-M1–M3 accepted. P6-M2 Security re-review ACCEPT; original
  M-1/M-2/L-1 descriptions are absent, so exact finding-to-fix mapping could
  not be audited, while the fresh review found the current controls sound.
  Existing recorded M2 checks include Vitest 156 and Playwright 150 (3 viewport);
  Creator-panel E2E is not present and live `api:verify` has not run. The public
  template-catalog request no longer sends Creator cookies (`credentials: 'omit'`).
- **P6-M3 backend (guest upload capability) durumu:** anonim `with-media` oluşturma
  (PendingMedia + 10 dk HttpOnly/Secure/SameSite=Strict `__Host-davetiye-memory-upload`
  çerezi, yalnız HMAC digest saklanır), intent/status/finalize yalnız bu capability ile;
  başka memory/davetiye/süresi dolmuş/tüketilmiş/yabancı capability tek tip 404. Yeni
  entitlement anahtarları `maxGuestImages/Videos/ImageSizeMb/VideoSizeMb/VideoDurationSeconds`
  (kabul edilen 100/10/10/100/60 tüm planlarda seed; hard ceiling 250/10/10/250/180, Creator
  tavanını aşmaz; mevcut DB'ler migrator seed'iyle tamamlanır, migration yok). Süresi dolan
  PendingMedia anıları medya yaşam döngüsü job'ında Abandoned olur (açık upload Rejected,
  Ready PendingDeletion; bayt ve kota PD-16 gereği tutulur). Dağıtım gereği:
  `MemoryUploadCapabilities__HmacKeyBase64` (>=256 bit) ve `__HmacKeyVersion` ayarlanmalı.
  Post-fix checks: solution build 0 warnings/errors, Unit 376/376, Architecture
  79/79; backend owner ran guest upload integration 39/39 and unit 15/15.
  Independent Tester/Reviewer and Security both ACCEPT. A Low global
  idempotency-key collision finding was fixed by translating only the named
  unique-index collision to a safe `idempotency_conflict`; deterministic tests
  cover concurrent cross-invitation reuse and Guest-vs-Creator collision.
  A fresh broader 107-test filtered run passed all tests but was manually
  interrupted during fixture cleanup, so its cleanup result is not counted.
  P6-M4 complete (4/6): Security and Tester/Reviewer ACCEPT. PendingMedia text/emoji projects immediately; only Ready Guest media is included. Empty rows are excluded before count/pagination. Cloudflare-disabled config hides upload and create/intent return 503 before quota writes. Backend build 0 warnings/errors; focused PostgreSQL projection/provider tests pass with clean teardown; Unit 19/19; Media architecture 6/6; frontend 165/165 plus lint/typecheck/build/API check. Real-provider acceptance remains Phase 11.
- **Guest medya kotaları (Orchestrator önerisi, Fatih 2026-10-05'te "tamam
  devam et" ile kabul etti):** anı başına en çok 3 medya; davetiye başına 100
  guest fotoğraf ve 10 guest video; fotoğraf en çok 10 MB; video en çok 100 MB
  ve 60 saniye. Değerler plan entitlement kataloğundan gelir (ADR-0004
  katalog değişikliği; başlangıçta tüm planlara aynı değer, paket farkı
  Phase 8'de); kod hard ceiling'leri Creator tavanlarını aşamaz. Creator
  kotasından ayrıdır (PD-06).
- **Guest EXIF/GPS:** fotoğraflar için Worker normalizasyonuyla metadata
  temizliği garantilidir (ADR-0005); guest videoları için metadata temizliği
  MVP'de garanti edilmez ve UI/dokümanda böyle belirtilir.
- **Abandoned kayıt saklama (Fatih, 2026-10-05):** süresi dolmuş Abandoned
  anı/capability metadata'sı varsayılan olarak 30 gün sonra temizlenir. Değer
  DB `SystemSetting` ile değiştirilebilir. Temizlik MediaAsset/PendingUpload,
  provider byte'ları veya Guest quota durumunu silmez; bunlar PD-16 gereği
  davetiye kalıcı silinene kadar saklanır.

## Milestone'lar

| No | Milestone | Dependency | Roles | Acceptance criteria | Implementation / verification |
| ---: | --- | --- | --- | --- | --- |
| 1 | Memory contract | Faz 3, Faz 4 | Architect, Backend, Database, Security | Config, visibility, submission state, ownership/retention schema review geçer; PD-04/PD-06 varsayılmaz; Creator-only/Public projection ayrılır. | Prior handoff; accepted, implementer/reviewers not recorded |
| 2 | Text/emoji submission | 1 | Backend, Frontend, Security | Active/module/entitlement/rate-limit kontrollü anonymous text, opsiyonel display name; length/count/protocol/stored-XSS testleri geçer; email/phone zorunlu değil. | Backend/Frontend; Security p6_m2_security ACCEPT; historical IDs not mapped |
| 3 | Guest upload capability | 1, 2, Faz 4 portları | Backend, Security | Tek pending memory'ye bağlı kısa ömürlü intent/finalize; başka memory/invitation'a replay yok; kalıcı guest management token yok. | Backend p5_m5_backend; Tester/Reviewer p6_m3_tester_review ACCEPT; Security p6_m2_security ACCEPT; Low fixed |
| 4 | Verified finalize/public projection | 3 | Backend, Frontend, Security | Public PendingMedia text/emoji is immediate; Guest media appears only when Ready; empty rows are excluded before total/pagination; inactive and entitlement gates apply. | Backend p6_m4_backend + Frontend p5_m5_frontend; Security and Tester/Reviewer ACCEPT after fixes; PostgreSQL projection/race tests pass |
| 5 | Creator moderation | 2, 3, 4 | Backend, Frontend, UI/UX | Owner list, hide/delete, provider deletion coordination; no Guest edits/deletes after finalize; Creator BOLA and deletion retry tests pass. | Complete — Backend p6_m4_backend; frontend p5_m5_frontend; Tester/Reviewer p6_m3_tester_review ACCEPT; Security p6_m2_security ACCEPT. PostgreSQL M5 2/2, deletion retry 1/1, OpenAPI 1/1, architecture 12/12, Guest Media unit 15/15, solution build 0 warnings/errors; frontend moderation 6/6, full suite 173 tests. Broad three-class PostgreSQL run was stopped after >2 minutes without output; not counted. |
| 6 | Abuse/privacy kapanışı | 1–5 | Tester, Security, Reviewer | Upload/text spam, quota race, media failure, a11y/security testleri geçer; cost/abuse control, XSS, capability ve privacy doğrulanır. Yerel provider-contract/failure testleri geçer; canlı Cloudflare Worker/R2/Stream kabulü kullanıcı kararıyla Phase 11'e ertelenmiştir. | Complete — Security and Tester/Reviewer ACCEPT. Retention re-review confirms 30-day configurable cleanup removes only aged Abandoned memory/capability metadata in bounded batches; MediaAsset, PendingUpload, provider bytes, and Guest quota remain until permanent invitation purge per PD-16. Final focused PostgreSQL checks 4/4; retention/quota PostgreSQL regression 2/2; Memories Playwright/axe 9/9 across desktop/320px/200% zoom; frontend Vitest 175/175, lint/typecheck/build/API check; Unit 103/103, retention policy 8/8, Architecture 12/12 (including Memories boundary 6/6), solution build 0 warnings/errors. Real Cloudflare Worker/R2/Stream acceptance remains Phase 11. |

## Milestone Bağımlılık Grafiği

```text
M1 ──┬──> M2 ──┐
     └──> M3 ──┴──> M4 ──> M5 ──> M6
```

M2 (text/emoji) ve M3 (guest upload) M1'den sonra farklı submission
yüzeylerini etkilediği için paralel yürütülebilir; M4 (verified finalize)
her ikisine de bağımlıdır ve serieldir.

## Beklenen Uzman Rolleri

- **Architect:** ADR-0002/0005 sınırları, memory contract review.
- **Backend + Database:** schema, capability, finalize/projection,
  moderation.
- **Security:** stored-XSS, capability replay, threat model (media/input/
  privacy) karşılaştırması.
- **Frontend + UI/UX:** submission UX, Creator moderation ekranı, a11y.
- **Tester:** XSS, malformed/oversize media, cross-capability, rate-limit,
  concurrency, provider processing/failure testleri.
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
- Stored-XSS, malformed/oversize media ve cross-capability replay testleri
  geçer.
- Provider processing/failure sözleşmeleri ve retry senaryoları Faz 4 media
  portları üzerinden fake/local provider ile testli. Canlı Cloudflare
  Worker/R2/Stream kabulü kullanıcı kararıyla Phase 11 P11-M6/M8'e ertelendi.
- Mobile/a11y testleri geçer.
- CI clean checkout'ta bu kontrolleri tekrarlar.

## Faz Tamamlanma Kriterleri

- Exact visibility davranışı (Creator-only/Public) accepted karara göre
  implement edilmiştir; hiçbir seçenek varsayılmamıştır.
- Provider cleanup çalışır durumdadır; Critical/High bulgu yoktur.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer onayı
  vardır.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; visibility kararı, quota'lar, provider
evidence, bilinen residual risk ve retention linkage günceller.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 6 uygulama izni değildir. Faz 6, ancak Faz 1–4
fiilen tamamlanıp bağımsız doğrulandıktan **ve** Fatih bu planı (veya
doğrulanmış önceki fazlar sonrası güncellenmiş halini) açıkça onayladıktan
sonra başlayabilir. Faz 6'nın tamamlanması, media-rich public invitation ve
final deletion graph'ını mümkün kılar; Gift/commerce veya başka faz yalnız
explicit approval ile başlar.
