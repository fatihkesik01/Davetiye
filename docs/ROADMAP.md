# Davetiye — Ana Ürün ve Teslimat Yol Haritası

Durum: **Current planning baseline (mevcut planlama taban çizgisi) —
implementation yetkisi vermez**  
Son güncelleme: **2026-10-07**
Kapsam: Mevcut repository durumundan gerçek production ve ticari lansmana
kadar MVP teslimat yolu

> Bu belge bütün yolu ve yüksek seviyeli durumu gösterir. Bir phase'in burada
> yer alması veya `PHASE_*_PLAN.md` dosyasının bulunması o phase'i uygulama
> izni değildir. Yalnız Fatih'in açıkça onayladığı phase uygulanabilir.

### Etiket sözlüğü (bu belgede tekrar eden İngilizce durum/etiket kelimeleri)

Bu belgede aynı birkaç İngilizce kelime defalarca geçiyor (tablo hücrelerinde
tekrar tekrar Türkçesini yazmak yerine tek seferde burada açıklanıyor):

- **COMPLETED** — Tamamlandı
- **NOT STARTED** — Başlamadı
- **WAITING FOR FATIH** — Fatih'in onayı bekleniyor
- **APPROVED — NOT STARTED** — Fatih onayladı; implementasyon henüz başlamadı
- **IN PROGRESS** — Onaylı phase'in implementasyonu veya kalite kapıları sürüyor
- **Accepted** — Kabul edildi
- `[DECISION REQUIRED]` — Karar gerekiyor (Fatih'ten)
- `[OWNER ACTION]` — Senin (Fatih'in) yapman gereken bir şey
- `[EXTERNAL SETUP]` — Dışarıda (üçüncü parti serviste) kurulum gerekiyor
- `[PAYMENT / ACCOUNT]` — Ödeme/hesap açma işlemi
- `[LEGAL / COMMERCIAL]` — Hukuk/ticaret onayı gerekiyor
- `[LEGAL REVIEW REQUIRED]` — Hukuki inceleme gerekiyor
- `[EXTERNAL VERIFICATION REQUIRED]` — Dış kaynaktan doğrulama gerekiyor (bkz. §19)
- `[ENGINEERING]` — Mühendislik/teknik iş
- `[SECURITY]` — Güvenlik
- `[VERIFICATION]` — Doğrulama/test kanıtı
- `[PRIVACY]` — Gizlilik/KVKK
- `[GO-LIVE GATE]` — Canlıya geçiş kapısı (son onay noktası)

---

# PART A — ROADMAP FOR FATIH (BÖLÜM A — FATİH İÇİN YOL HARİTASI)

## 1. Proje nedir?

Davetiye; teknik veya tasarım bilgisi olmayan bir kullanıcının bir şablon
seçip bilgilerini doldurarak birkaç dakika içinde profesyonel, mobil uyumlu
bir dijital davetiye web sayfası hazırlamasını sağlayacak bir platformdur.

Creator (davetiye sahibi) davetiyesini oluşturur ve yönetir. Davetliler hesap
açmadan public link üzerinden davetiyeyi görür; Creator'ın açtığı özelliklere
göre RSVP yanıtı, anı, fotoğraf/video veya hediye rezervasyonu bırakabilir.
Super Admin ise platformun paket, ödeme, kullanıcı ve operasyon tarafını
yönetir. Ürün bir sürükle-bırak tasarım editörü değildir; hızlı ve güvenli
“şablon seç → bilgileri gir → önizle → yayınla” deneyimidir.

## 2. Fatih progress overview (İlerleme özeti)

Durum (2026-10-07 itibarıyla repository ve testlerle doğrulandı):

| Phase | Kısa ad | Durum |
| ---: | --- | --- |
| 0 | Product & Architecture | **COMPLETED** |
| 1 | Repository & Production Foundation | **COMPLETED** (17/17) |
| 2 | Creator Onboarding, Drafts & Template Catalog | **COMPLETED** (8/8) |
| 3 | Creator Content, Publishing & Public Invitation | **COMPLETED** (8/8) |
| 4 | Creator Media & Gated Delivery | **COMPLETED** (8/8) |
| 5 | RSVP & Attendance Insights | **COMPLETED** (6/6) |
| 6 | Memories & Guest Media | **COMPLETED** (6/6) |
| 7 | Gift Registry | **COMPLETED** (6/6) |
| 8 | Commerce, Plans & Transactional Email | **COMPLETED** (7/7) |
| 9 | Super Admin & Platform Governance | **COMPLETED** (7/7) |
| 10 | Privacy, Retention & Integrated MVP Hardening | **COMPLETED** (7/7) |
| 11 | Production Readiness & Business Launch | **NOT STARTED** |

**12 phase'in 11'i tamamlandı (Phase 0–10).** Phase 2–10'un bütün gerçek
provider kabulleri (Cloudflare, iyzico, Resend, Google production), clean-
checkout CI ve VPS deployment Phase 11'e ertelenmiştir. Her phase'in milestone
ve kanıt ayrıntısı kendi `docs/PHASE_N_PLAN.md` dosyasındadır.

## 3. Where are we now? (Şu an neredeyiz?)

- **Aktif phase:** Yok. Phase 10 tamamlandı; Phase 11 ayrıca açık onay bekliyor.
- **Güncel çalışma noktası ve son doğrulama kanıtı:** `docs/AI_HANDOFF.md`.
- Phase 1'den taşınan feature-activation gate'leri Part B §6'da doğru
  phase'e bağlıdır.

## 4. Fatih's actions (Senin yapman gerekenler)

### Şimdi

Phase 11'in başlaması için Part B §16 / `docs/PHASE_11_PLAN.md`'deki
`[DECISION REQUIRED — FATIH]` maddeleri ve aşağıdaki hesap/hukuk aksiyonları.

### Yaklaşan phase'lerde

| Etiket | Aksiyon | Ne zamana kadar? | Yapılmazsa etkisi |
| --- | --- | --- | --- |
| [DECISION REQUIRED] | Part B §4'teki product kararlarını ilgili phase başlamadan cevaplamak. | Her kararın “son cevap kapısı” sütununda belirtilir. | İlgili milestone başlamaz; diğer bağımsız işler devam edebilir. |
| [EXTERNAL SETUP] | Gerekirse localhost Google OAuth acceptance için ayrı development client/config hazırlamak. | Phase 2 auth/onboarding canlı Google testi öncesi; production projesi değildir. | Otomatik contract testleri sürebilir, gerçek localhost OAuth acceptance bekler. |
| [OWNER ACTION] | Cloudflare hesabı, R2/Images/Stream kaynakları, scoped credentials ve hesaba özel upload/CPU limitlerini hazırla ve incele. | Fatih'in 2026-10-04 kararıyla Phase 11 P11-M6 öncesine ertelendi. | Phase 4/5 local engineering devam edebilir; gerçek-provider media acceptance ve production enablement Phase 11'e kadar bloke kalır. |
| [PAYMENT / ACCOUNT] | iyzico sandbox/merchant sürecini başlatmak ve güncel kabul şartlarını doğrulamak. | Fatih'in kararıyla Phase 11 production provider acceptance öncesine ertelendi. | Local fake-provider and fixture tests Phase 8'de yapılır; actual provider acceptance ve production payments Phase 11'e kadar bloke kalır. |
| [EXTERNAL SETUP] | Resend hesabı/test domain'i veya production sending domain planını hazırlamak. | Fatih'in kararıyla Phase 11 production provider acceptance öncesine ertelendi. | Local adapter/outbox tests Phase 8'de yapılır; actual deliverability acceptance Phase 11'e kadar bloke kalır. |

### Production'a yaklaşırken

| Etiket | Aksiyon | Neden / zaman | Yapılmazsa etkisi |
| --- | --- | --- | --- |
| [OWNER ACTION] | Production domain'ini seç ve satın al. | Phase 11 başlamadan önce; DNS, HTTPS, Google OAuth, email ve public URL'ler buna bağlıdır. | Gerçek production launch ve Google login bloke olur. |
| [EXTERNAL SETUP] | Domain DNS/Cloudflare erişimini, Google Cloud OAuth projesini, Resend domain'ini ve Cloudflare production kaynaklarını sahiplen. | Phase 11 external setup milestone'ı. | Production entegrasyonları açılamaz. |
| [LEGAL / COMMERCIAL] | İşletme/merchant yapısı, fiyatların vergi/gösterim şekli, satış/iptal/iade metinleri ve iyzico güncel onboarding belgelerini yetkin kaynaklarla doğrula. | Production ödeme kabulünden önce. | Ücretli launch yapılamaz. **[EXTERNAL VERIFICATION REQUIRED]** |
| [LEGAL / COMMERCIAL] | KVKK/privacy, hizmet şartları, açık rıza/aydınlatma, retention ve gerekli ticari metinleri hukuk uzmanına doğrulat. | Public production trafiğinden önce. | Go-live gate geçmez. **[LEGAL REVIEW REQUIRED]** |
| [OWNER ACTION] | `support@…`, `noreply@…`, operasyonel uyarı adresleri ve incident iletişim sahibini belirle. | Email/operasyon production konfigürasyonundan önce. | Destek, bounce ve incident akışı eksik kalır. |
| [EXTERNAL SETUP] | Şifreli off-site backup hedefi ve erişim politikasını seç. | Canlı veriden önce. | Production-ready sayılmaz. |
| [GO-LIVE GATE] | Kontrollü gerçek düşük tutarlı ödeme ve final production smoke testine onay ver. | Diğer bütün gate'ler yeşil olduktan sonra. | Ticari launch yapılmaz. |

## 5. External service durumu

| Servis / konu | Repository'deki kabul edilmiş seçim | Bugünkü durum |
| --- | --- | --- |
| Hosting | Hostinger VPS + Docker Compose + Nginx | VPS mevcut; **Davetiye deploy edilmemiş**. Aynı VPS'teki Lora'ya dokunulamaz. |
| Database | PostgreSQL | Local/test ve Compose foundation hazır; production DB yok. |
| Domain / HTTPS | Henüz domain seçilmedi | **Kurulmadı.** Gerçek kullanıcı trafiği plain HTTP ile açılamaz. |
| Google login | Google OAuth | Local/foundation hazır; production domain + HTTPS ve production OAuth config bekliyor. |
| Fotoğraf | Cloudflare R2 + Images Transformations | Hesap/bucket/token production kurulumu repository'de yok. |
| Video | Cloudflare Stream | Production account/config/webhook kurulumu yok. |
| Transactional email | Resend | Kodda provider sınırı ve dev sender var; production domain/key yok. |
| Ödeme | iyzico | Provider kararı var; gerçek gateway, merchant approval ve credentials yok. |
| Off-site backup | Günlük şifreli PostgreSQL logical backup | Runbook var; hedef/provider ve production scheduler henüz seçilmedi. |
| Hukuk/ticaret | KVKK/privacy ve ticari doğrulama gerekli | Legal metinler ve güncel merchant gereklilikleri henüz doğrulanmadı. |

## 6. Proje ne zaman gerçekten bitti sayılacak?

Yalnız “site açılıyor” veya “kod deploy edildi” yeterli değildir. MVP ancak:

1. Phase 0–11 completion gate'leri doğrulanmış,
2. PRODUCT kapsamındaki Creator, Guest ve Super Admin akışları çalışıyor,
3. backlog özellikleri yanlışlıkla MVP'ye eklenmemiş,
4. Critical/High bulgu yok ve kalan Medium bulguların sahibi/disposition'ı var,
5. production domain, HTTPS ve provider hesapları gerçek credentials ile
   fail-closed çalışıyor,
6. ödeme, email, medya, backup/restore, monitoring ve incident temelleri gerçek
   ortamda test edilmiş,
7. hukuk/ticaret ve privacy gate'leri yetkin kişilerce kapatılmış,
8. mobil/tablet/desktop ve accessibility kabul testleri geçmiş,
9. kontrollü production E2E smoke testi başarılı,
10. Fatih nihai go-live onayını vermiş

olduğunda **PRODUCTION READY** kabul edilir. Bundan sonra trafik açma ayrı ve
bilinçli bir **GO-LIVE** kararıdır.

## 7. Final go-live gate (canlıya geçiş kapısı) — kısa görünüm

- [ENGINEERING] Bütün MVP akışları ve database migration'ları release
  candidate üzerinde yeşil.
- [EXTERNAL SETUP] Domain, DNS, TLS, Cloudflare, Google OAuth, Resend ve iyzico
  production yapılandırmaları tamam.
- [OWNER ACTION] Fatih provider hesaplarının sahibi, billing/uyarı adresleri ve
  incident iletişim kişisi olarak gereken seçimleri yaptı.
- [LEGAL / COMMERCIAL] KVKK/privacy/terms ve ödeme-ticaret metinleri uzmanlarca
  doğrulandı; merchant production approval alındı.
- [SECURITY] Auth/AuthZ/BOLA, Admin MFA, CSRF/CORS/CSP, rate limits, upload ve
  webhook güvenliği, secrets, scan'ler ve backup security gate'i geçti.
- [VERIFICATION] Restore drill, rollback rehearsal, monitoring/alerting ve
  kontrollü production smoke testleri kanıtlandı.
- [GO-LIVE GATE] Açık zorunlu gate yok; Fatih trafik/duyuru başlangıcını onayladı.

## 8. Bu dosyalar ne işe yarıyor?

### `docs/PRODUCT.md`

**NE ANLATIYOR:** Ürünün ne yapacağını, kullanıcı türlerini, MVP kapsamını,
paket başlangıç değerlerini ve backlog sınırını.  
**FATIH NE ZAMAN BAKMALI:** Bir davranış, fiyat, limit veya kapsam kararı
verirken.  
**AI AGENTLAR İÇİN ROLÜ:** Ürün kararlarında en üst repository authority.  
**SOURCE OF TRUTH MI?:** **Evet — product source of truth.**

### `docs/ARCHITECTURE.md`

**NE ANLATIYOR:** React, ASP.NET Core, PostgreSQL, provider ve deployment
sınırlarının nasıl kurulduğunu.  
**FATIH NE ZAMAN BAKMALI:** Teknik yaklaşımın neden böyle olduğunu veya bir
provider/hosting değişikliğinin etkisini anlamak istediğinde.  
**AI AGENTLAR İÇİN ROLÜ:** Kabul edilmiş MVP teknik kısıtları.  
**SOURCE OF TRUTH MI?:** **Evet — PRODUCT ve accepted ADR'ların altında teknik
authority.**

### `docs/AI_WORKFLOW.md`

**NE ANLATIYOR:** Orchestrator ve specialist agentların provider'dan bağımsız
olarak nasıl planlayacağını, uygulayacağını, test/review yapacağını ve onay
kapısında duracağını.  
**FATIH NE ZAMAN BAKMALI:** AI çalışma düzeni, phase onayı veya “done” iddiasının
nasıl doğrulandığını görmek istediğinde.  
**AI AGENTLAR İÇİN ROLÜ:** Ortak çalışma protokolü.  
**SOURCE OF TRUTH MI?:** **Evet — delivery process source of truth.**

### `docs/AI_HANDOFF.md`

**NE ANLATIYOR:** Bugün nerede kalındığını; aktif/onaylı phase'i, son kanıtları,
blocker ve sıradaki önerilen işi kısa biçimde.  
**FATIH NE ZAMAN BAKMALI:** Yeni session/provider başlatırken veya “şu an tam
olarak neredeyiz?” diye bakarken.  
**AI AGENTLAR İÇİN ROLÜ:** Hızlı başlangıç snapshot'ı; körlemesine güvenilmez,
git/code/tests ile doğrulanır.  
**SOURCE OF TRUTH MI?:** **Yaşayan durum kaydıdır; history veya implementation
kanıtı değildir.** ROADMAP bütün yolu, HANDOFF yalnız bugünkü çalışma noktasını
gösterir.

### `docs/ROADMAP.md`

**NE ANLATIYOR:** Baştan gerçek production/business launch'a kadar bütün
phase'leri, bağımlılıkları, kararları ve yüksek seviyeli durumu.  
**FATIH NE ZAMAN BAKMALI:** Büyük resmi, sıradaki aşamaları ve kendi yaklaşan
aksiyonlarını takip ederken.  
**AI AGENTLAR İÇİN ROLÜ:** Yeni phase planlarının türetildiği uzun vadeli teslimat
haritası. Product/ADR kararı üretmez.  
**SOURCE OF TRUTH MI?:** **Evet — roadmap ve yüksek seviyeli phase status için;
PRODUCT/ADR/ARCHITECTURE'ın üstünde değildir.**

### `docs/PHASE_1_PLAN.md` (tamamlanmış fazlar için: aynı dosyanın Milestone'lar tablosu)

**NE ANLATIYOR:** Ayrı bir "execution" dosyası yoktur (2026-09-29'da bu
model terk edildi — bkz. §17). Bir faz tamamlandığında, o fazın kendi
`PHASE_N_PLAN.md`'sindeki Milestone'lar tablosu güncellenerek kimin
uyguladığını, kimin review ettiğini ve bulgu sayılarını (Critical/High/
Medium/Low) kaydeder — birkaç kelimelik hücre/not olarak, ayrı bir uzun
anlatı bölümü değil (`docs/AI_WORKFLOW.md` §13.3).  
**FATIH NE ZAMAN BAKMALI:** Bir phase'in neden tamamlandı sayıldığını
incelemek istediğinde.  
**AI AGENTLAR İÇİN ROLÜ:** Phase-level attribution/finding kaydı; ROADMAP
durumunu ve HANDOFF özetini besler.  
**SOURCE OF TRUTH MI?:** **Milestone tablosu kaydıdır; gerçek git/code/tests
ile doğrulanır. Rutin build/test detayları (tam sayılar, "0 warning" gibi)
burada tekrar yazılmaz — bunlar gerçek komut çalıştırılarak yeniden
kanıtlanır.**

### `docs/PHASE_2_PLAN.md` ve gelecekteki `PHASE_*_PLAN.md`

**NE ANLATIYOR:** Bir phase uygulanmadan önceki ayrıntılı, onaya sunulan planı.  
**FATIH NE ZAMAN BAKMALI:** Bir phase'i onaylamadan önce kapsam ve milestone'ları
incelemek için.  
**AI AGENTLAR İÇİN ROLÜ:** ROADMAP'teki phase'i, o günkü repo gerçeğine göre
uygulanabilir milestone planına çevirir.  
**SOURCE OF TRUTH MI?:** **Yalnız ilgili onaylı phase'in planıdır.** Eski planın
status satırı güncel execution gerçeğinin yerine geçmez; planın varlığı izin
değildir.  
### `docs/PHASE_0_PLAN.md`

**NE ANLATIYOR:** MVP domain/trust boundary'lerini ve PD-01–PD-13 açık product
decision register'ını.  
**FATIH NE ZAMAN BAKMALI:** Açık bir product kararının tam bağlamını görmek için.  
**AI AGENTLAR İÇİN ROLÜ:** Phase 0 teknik baseline ve unresolved-decision guard.  
**SOURCE OF TRUTH MI?:** **Evet — accepted baseline; açık kararlar accepted
değildir.**

### `docs/adr/*`

**NE ANLATIYOR:** Önemli teknik kararların ne olduğunu ve neden seçildiğini.  
**FATIH NE ZAMAN BAKMALI:** “Neden modular monolith?”, “publicCode neden yetki
değil?”, “webhook neden idempotent?” gibi sorularda.  
**AI AGENTLAR İÇİN ROLÜ:** Kabul edilmiş teknik kararlar ve değişiklikte review
kapısı.  
**SOURCE OF TRUTH MI?:** **Evet — accepted technical decision; PRODUCT'ı
değiştiremez.**

### `docs/THREAT_MODEL.md`

**NE ANLATIYOR:** Korunan varlıkları, saldırı senaryolarını, trust boundary'leri
ve zorunlu security gate'lerini.  
**FATIH NE ZAMAN BAKMALI:** Güvenlik riskleri ve “neden bu kontrol gerekli?”
sorusunda.  
**AI AGENTLAR İÇİN ROLÜ:** Her security-relevant milestone'un review matrisi.  
**SOURCE OF TRUTH MI?:** **Accepted security baseline.**

### `docs/UX_FLOWS.md` ve `docs/PHASE_1_PLAN.md`

**NE ANLATIYOR:** Creator, Guest ve Admin akışlarını; responsive,
accessibility, autosave ve lifecycle UX sözleşmelerini.  
**FATIH NE ZAMAN BAKMALI:** Kullanıcıların üründe ne göreceğini ve hangi
akıştan geçeceğini anlamak istediğinde.  
**AI AGENTLAR İÇİN ROLÜ:** UI/UX kabul sınırı; product kararını aşmaz.  
**SOURCE OF TRUTH MI?:** `UX_FLOWS` accepted baseline; `PHASE_1_PLAN.md` §10
(eski `PHASE_1_UX_FOUNDATION`) accepted uygulama/interaction contract'ıdır.

### `docs/DEPLOYMENT.md`

**NE ANLATIYOR:** Hostinger VPS bağlantısı, Davetiye deploy şekli ve aynı
sunucudaki Lora'yı koruma kuralları.  
**FATIH NE ZAMAN BAKMALI:** Deploy, reset veya production operasyonu öncesinde.  
**AI AGENTLAR İÇİN ROLÜ:** VPS operasyonları için zorunlu güvenlik runbook'u.  
**SOURCE OF TRUTH MI?:** **Evet — deployment/VPS operations.**

### `docs/DATABASE_MIGRATIONS.md` ve `docs/BACKUP_RESTORE_RUNBOOK.md`

**NE ANLATIYOR:** Migration ownership/upgrade yaklaşımı ile backup/restore
adımlarını.  
**FATIH NE ZAMAN BAKMALI:** Production veri değişikliği, backup veya recovery
hazırlığı incelenirken.  
**AI AGENTLAR İÇİN ROLÜ:** Database operation ve recovery runbook'ları.  
**SOURCE OF TRUTH MI?:** **İlgili operasyon prosedürü için evet; ürün davranışı
tanımlamaz.**

### `docs/agents/*` ve provider adapter'ları

**NE ANLATIYOR:** Architect, Backend, Reviewer gibi specialist rollerin ortak
talimatlarını. `.claude/`, `.cursor/` ve `.codex/` kopya/wrapper dosyalarıdır.  
**FATIH NE ZAMAN BAKMALI:** AI rollerinin sorumluluğunu merak ettiğinde.  
**AI AGENTLAR İÇİN ROLÜ:** Vendor-neutral canonical agent tanımları.  
**SOURCE OF TRUTH MI?:** `docs/agents/*` **evet**; provider-specific üretilmiş
adapter'lar **hayır**.

## 9. Source of truth map (Gerçeğin kaynağı haritası)

```text
FATİH + docs/PRODUCT.md
  “Ne yapıyoruz; product davranışı ve MVP sınırı nedir?”
                     │
                     ▼
accepted ADR'lar + docs/ARCHITECTURE.md
  “Bunu teknik olarak hangi kabul edilmiş sınırlarla yapıyoruz?”
                     │
                     ▼
docs/ROADMAP.md
  “Bugünden launch'a hangi büyük aşamalardan geçiyoruz?”
                     │
                     ▼
onaylı PHASE planı → PHASE execution + docs/AI_HANDOFF.md
  “Bu phase nasıl yapılacak?” → “Şu anda tam olarak neredeyiz?”
                     │
                     ▼
GIT + CODE + MIGRATIONS + GERÇEK TEST/CI SONUÇLARI
  “Gerçekte ne yapılmış ve çalışıyor?”
```

Çelişki çözme sırası: `PRODUCT.md` → Fatih'in açıkça kabul ettiği product
kararları → accepted ADR'lar → `ARCHITECTURE.md` → accepted baseline ve o an
onaylı phase planı → specialist önerileri. ROADMAP bu sırayı değiştirmez.
Execution/HANDOFF “done” diyebilir; git, gerçek kod ve testler doğrulamıyorsa
tamamlanmış sayılmaz.

---

# PART B — DETAILED DELIVERY ROADMAP (BÖLÜM B — DETAYLI TESLİMAT YOL HARİTASI)

## 1. Bu roadmap nasıl kullanılacak?

- **COMPLETED:** Completion kriterleri gerçek kanıtla geçti ve bağımsız gate
  kapandı.
- **IN PROGRESS:** Kullanıcı tarafından onaylanmış phase'te en az bir milestone
  aktif.
- **NOT STARTED:** Planlanmış fakat implementation başlamamış.
- **BLOCKED:** Onaylı phase güvenli biçimde ilerleyemiyor ve blocker kayıtlı.
- **WAITING FOR FATIH:** Açık product kararı veya phase onayı bekleniyor.

Her phase başlamadan önce repository yeniden doğrulanır ve bu roadmap'teki
phase bölümü `docs/PHASE_TEMPLATE.md` ile güncel, ayrıntılı bir phase planına
dönüştürülür. Gelecekteki milestone detayları yön gösterir; o günkü code,
provider API ve kabul edilmiş kararlarla yeniden reconcile edilmeden doğrudan
implementation talimatı sayılmaz.

## 2. Kalıcı delivery kuralları

1. Yalnız Fatih'in açıkça onayladığı phase uygulanır.
2. Ürün kararı gereken milestone tahminle açılmaz; `[DECISION REQUIRED — FATIH]`
   olarak bekler. Aynı phase'teki bağımsız işler sürebilir.
3. Her milestone implement → test → gerekiyorsa Security → Reviewer → fix →
   reverify döngüsünden geçer.
4. Public identifier Creator/Admin yetkisi değildir; authorization backend'de
   query-level ownership ve capability scope ile uygulanır.
5. Configurable ticari limitler DB-driven, security hard ceiling'leri code
   contract'ıdır.
6. Provider credential ve production secret'ları Git'e girmez.
7. Her phase sonunda execution state, `AI_HANDOFF.md` ve ROADMAP status'u
   birlikte fakat farklı ayrıntı seviyelerinde güncellenir.
8. Sonraki phase otomatik başlamaz.

## 3. Phase bağımlılık özeti

```text
Phase 0 ─► Phase 1 ─► Phase 2 ─► Phase 3 ─┬─► Phase 4 ─► Phase 6 ─┐
                                          ├─► Phase 5 ────────────┤
                                          └─► Phase 7 ────────────┤
                              Phase 3 ─► Phase 8 ─► Phase 9 ──────┤
                                                                  ▼
                                                               Phase 10
                                                                  ▼
                                                               Phase 11
```

Phase 4, Phase 5 ve Phase 7; Phase 3'ün public/lifecycle contracts'ı
sabitlendikten sonra teknik olarak kısmen paralel planlanabilir. Phase 6 guest
media nedeniyle Phase 4'e de bağlıdır. Shared `DbContext`/migration snapshot,
invitation, entitlement ve frontend renderer dosyaları tek owner üzerinden
serialize edilir. Phase 8 plan/grant kararlarına ve provider hazırlığına;
Phase 9 ise yöneteceği gerçek modüllere bağlıdır. Phase 10 bütün MVP
slicelarını birleştirmeden başlayamaz. Phase 11 yalnız release candidate
kapanışından sonra açılır.

## 4. Product decision durumu

PD-01–PD-17 ve Phase 2 başlangıç kararlarının (Organization hesap tipi kayıtta
seçilir ve sonradan dönüştürülemez; Google same-email için açık account-linking
UX; premium şablon Draft'ta seçilebilir, yalnız publish preflight engeller;
wizard sırası ve sekiz event type) **tamamı Fatih tarafından kabul edildi.**
Kabul metni ilgili phase planında kayıtlıdır:

| Karar | Kayıt yeri |
| --- | --- |
| Phase 2 başlangıç kararları | `docs/PHASE_2_PLAN.md` |
| PD-01, PD-03, PD-05, PD-07, PD-08, PD-09, PD-13, premium-template PD-04, timezone, harita, OG | `docs/PHASE_3_PLAN.md` |
| PD-06, PD-14, PD-15, PD-16 | `docs/PHASE_4_PLAN.md` |
| Memories PD-04 | `docs/PHASE_6_PLAN.md` |
| Gift PD-04, PD-10, PD-17 | `docs/PHASE_7_PLAN.md` |
| PD-02, PD-12 | `docs/PHASE_8_PLAN.md` |
| PD-11 | `docs/PHASE_9_PLAN.md` |

**Açık kalan:** Legal metinler, kesin retention süreleri ve Phase 11
kararları (bkz. §16).

## 5. Phase 0 — Product & Architecture Baseline

**Durum:** COMPLETED  
**Amaç:** MVP ürün kapsamını, domain/trust boundary'lerini, provider
seçimlerini, UX akışlarını, tehditleri ve açık kararları (PD-01–PD-13)
kabul edilmiş bir baseline'a dönüştürmek — feature içermez.  
**Bağımlılık:** Ürün keşfi ve Fatih'in kabul ettiği kararlar.  
**Ne mümkün kıldı:** Güvenli repository foundation implementasyonu (Phase 1).

**Detaylı plan / milestone kanıtı:** `docs/PHASE_0_PLAN.md`.

## 6. Phase 1 — Repository & Production Foundation

**Durum:** COMPLETED — 17/17 milestone unit  
**Amaç:** Feature içermeyen fakat production şekline uygun çalışan React,
ASP.NET Core, PostgreSQL, security, CI, Compose/Nginx ve runbook temelini
kurmak.  
**Bağımlılık:** Faz 0'ın kabulü.  
**Ne mümkün kıldı:** Creator draft/template feature geliştirme (Phase 2).

**Detaylı plan / milestone kanıtı:** `docs/PHASE_1_PLAN.md`.

**Phase 1'den taşınan future gate'ler:** Phase 3–10'a bağlananlar (grant
AccountId doğrulaması, media hard ceiling'ler, inbox payload minimizasyonu,
email outbox retry, ban anında session revoke, hesap silmede Ban/audit
korunması) ilgili fazlarda kapatıldı. Açık kalan: trusted-proxy CIDR,
session telemetry/rate limit ve forwarded-IP davranışı production topolojisine
göre Phase 11'de kapanır.

## 7–15. Phase 2–10 (tamamlandı)

Her fazın amacı, kabul edilen ürün kararları, milestone tablosu ve doğrulama
kanıtı yalnız kendi plan dosyasında tutulur; burada tekrarlanmaz.

| § | Phase | Durum | Fatih onayı | Ayrıntı | Phase 11'e ertelenen gate |
| ---: | --- | --- | --- | --- | --- |
| 7 | 2 — Creator Onboarding, Drafts, Templates & Preview | COMPLETED 8/8 | 2026-09-29 | `docs/PHASE_2_PLAN.md` | Gerçek Google OAuth acceptance |
| 8 | 3 — Entitlements, Publication Lifecycle & Public Invitation | COMPLETED 8/8 | 2026-10-01 | `docs/PHASE_3_PLAN.md` | — |
| 9 | 4 — Creator Media & Gated Delivery | COMPLETED 8/8 | 2026-10-03 | `docs/PHASE_4_PLAN.md` | Cloudflare hesap/limit ve gerçek-provider media kabulü (P11-M6/M8) |
| 10 | 5 — RSVP & Attendance Insights | COMPLETED 6/6 | 2026-10-05 | `docs/PHASE_5_PLAN.md` | — |
| 11 | 6 — Memories & Guest Media | COMPLETED 6/6 | 2026-10-05 | `docs/PHASE_6_PLAN.md` | Gerçek Cloudflare kabulü |
| 12 | 7 — Gift Registry | COMPLETED 6/6 | 2026-10-05 | `docs/PHASE_7_PLAN.md` | Clean-checkout CI |
| 13 | 8 — Commerce, Plans & Transactional Email | COMPLETED 7/7 | 2026-10-05 | `docs/PHASE_8_PLAN.md` | iyzico/Resend gerçek hesap kabulü; refund/dispute/chargeback kaynak doğrulaması |
| 14 | 9 — Super Admin & Platform Governance | COMPLETED 7/7 | 2026-10-06 | `docs/PHASE_9_PLAN.md` | Audit retention hukuki kararı |
| 15 | 10 — Privacy, Retention & Integrated MVP Hardening | COMPLETED 7/7 | 2026-10-06 | `docs/PHASE_10_PLAN.md` | Clean CI/push/VPS; Organization provider-cancellation adapter; gerçek tarayıcı 200% zoom ve manuel ekran okuyucu/kontrast; kesin hukuki retention süreleri |

## 16. Phase 11 — Production Readiness & Business Launch

**Durum:** NOT STARTED — ayrıca açık onay gerekir.
**Amaç:** Release candidate'ı gerçek domain, provider hesapları, güvenli VPS
operasyonları, hukuk/ticaret hazırlığı ve kontrollü production
doğrulamasıyla satışa açık canlı ürüne dönüştürmek.
**Açık karar:** `[DECISION REQUIRED — FATIH]` production domain; support/
sender adresleri; ticari/hukuki metinler; merchant/refund disclosure'ları;
kesin retention; OG personalization; launch zamanlaması.
**Detaylı plan:** `docs/PHASE_11_PLAN.md` (roadmap'teki son faz).

## 17. Phase belgeleri hakkında

- `PHASE_0_PLAN.md` authoritative baseline'dır (scope matrix, trust
  boundary'ler, PD register).
- `PHASE_1_PLAN.md` Faz 1 kaydı + aktif cross-phase UX contract'ıdır (§10).
- Diğer `PHASE_N_PLAN.md` dosyaları ilgili fazın onaylı planı ve milestone
  kaydıdır. Ayrı `PHASE_N_EXECUTION.md` açılmaz; tamamlanan faz kendi
  dosyasındaki Milestone tablosunda kısa hücre notuyla kaydedilir
  (`docs/AI_WORKFLOW.md` §13.3/§14).
- Bütün phase dosyaları `docs/PHASE_TEMPLATE.md` bölüm sırasını kullanır. Bir
  planın varlığı uygulama izni değildir.

## 18. Status update responsibility split (Durum güncelleme sorumluluk dağılımı)

| Katman | Sorumluluk | Tutmaması gereken ayrıntı |
| --- | --- | --- |
| ROADMAP | Nereye gidiyoruz, bütün phase planı, dependencies/decisions ve high-level status | Uzun günlük, tek tek komut çıktıları, finding transcript'i |
| PHASE EXECUTION | Aktif/ilgili phase'in milestone durumu, owner/reviewer/security attribution, finding ve verification evidence | Bütün gelecek ürün roadmap'inin kopyası |
| AI_HANDOFF | Yeni session/provider için kısa current state, active approval, next action, blockers ve son verification özeti | Kalıcı tarihçe veya bütün phase planı |
| Git/code/tests/CI | Gerçekte ne implement edildi ve çalışıyor | Product karar authority'si |

Bir milestone/phase gerçekten doğrulandığında Orchestrator sırasıyla execution
state'i, `AI_HANDOFF.md` snapshot'ını ve ROADMAP high-level status'unu günceller.
Aynı ayrıntılı test/finding metni üç dosyaya kopyalanmaz.

## 19. Official external references and re-verification rule (Resmi dış kaynaklar ve yeniden doğrulama kuralı)

Bu bağlantılar 2026-09-29 tarihinde roadmap hazırlarken kontrol edildi; provider
kuralları değişebileceği için ilgili phase'te yeniden doğrulanır:

- [iyzico webhook ve güncel signature dokümanı](https://docs.iyzico.com/en/advanced/webhook)
- [iyzico response signature validation](https://docs.iyzico.com/en/advanced/response-signature-validation)
- [Resend verified domains](https://resend.com/docs/dashboard/domains/introduction)
- [Resend DMARC guidance](https://resend.com/docs/dashboard/domains/dmarc)
- [Cloudflare R2 presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/)
- [Cloudflare R2 CORS](https://developers.cloudflare.com/r2/buckets/cors/)
- [Cloudflare Stream direct uploads/security](https://developers.cloudflare.com/stream/)
- [Cloudflare Stream webhook verification](https://developers.cloudflare.com/stream/manage-video-library/using-webhooks/)
- [Google OAuth app branding/authorized domains](https://support.google.com/cloud/answer/15549049)
- [Google domain verification](https://support.google.com/cloud/answer/13804266)

Bu referanslar product kararı değildir. Merchant onboarding belgeleri, şirket
tipi, legal/tax yükümlülükleri, plan/limit/fiyat ve verification gereksinimleri
hesap/ülke/API sürümüne göre değişebileceğinden ilgili maddeler
`[EXTERNAL VERIFICATION REQUIRED]` veya `[LEGAL REVIEW REQUIRED]` kalır.

## 20. Roadmap completion audit checklist (Tamamlanma denetim kontrol listesi)

Her roadmap güncellemesinde ve özellikle Phase 11 öncesinde Orchestrator şunları
kontrol eder:

- PRODUCT ve backlog sınırıyla çelişki yok.
- Accepted ADR/ARCHITECTURE kararları korunuyor.
- Completed iddiası gerçek git/code/test/CI ile kanıtlı.
- Unresolved product decision çözülmüş gibi yazılmamış.
- Fatih aksiyonu ile engineering aksiyonu ayrılmış.
- Deferred security finding doğru ilk-feature gate'ine bağlı.
- External provider veya hukuk şartı doğrulanmadan kesinleştirilmemiş.
- Phase planı uygulama izni gibi kullanılmıyor.
- ROADMAP, execution ve HANDOFF aynı ayrıntıyı gereksiz tekrar etmiyor.
- Provider-neutral agent workflow korunuyor.
- Yeni bir AI session yalnız repository'yi okuyarak current state'i yeniden
  doğrulayabiliyor.
