# Faz 11 — Production Readiness & Business Launch Planı

Durum: **TASLAK — Phase 11 başlamadı; Fatih'in açık onayını bekliyor**
Bağımlılık: **Faz 10'un doğrulanmış release candidate'ı; açık deploy/launch yetkilendirmesi; owner/legal/external aksiyonlar**

Bu planın varlığı Faz 11'in uygulanabileceği anlamına gelmez; Fatih'in ayrıca
açık onayı gerekir. Faz gerçek para, gerçek kullanıcı verisi ve production
altyapısı içerdiği için çok sayıda `[OWNER ACTION]`/`[LEGAL / COMMERCIAL]`/
`[EXTERNAL SETUP]` maddesi taşır; bunlar AI agent tarafından tek başına
yürütülemez.

## Amaç

Release candidate'ı gerçek domain, provider hesapları, güvenli VPS
operasyonları, hukuk/ticaret hazırlığı ve kontrollü production
doğrulamasıyla satışa açık canlı ürüne dönüştürmek. Bu faz var, çünkü
teknik deploy, merchant onayı, deliverability, backup, monitoring ve legal
hazırlık geçmeden ticari lansman olmaz: HTTPS domain altında gerçek
registration/login, Google, davetiye, guest, media, email, ödeme ve Admin
akışları çalışır.

## Kapsam

Aşağıdaki bütün launch workstream'leri ve final go-live gate: iş/ticari
hazırlık, domain/DNS/HTTPS, Google OAuth production, ödeme production,
Resend production email, Cloudflare/media production, VPS/veritabanı/
operasyon, güvenlik go-live kapısı, hukuk/gizlilik/SEO, kontrollü
production smoke ve go-live.

## Kapsam Dışı

Yeni MVP feature; backlog; Lora veya başka VPS projesinde değişiklik;
yetkisiz public traffic.

## Bağımlılıklar

Faz 10'un doğrulanmış release candidate'ı; açık deploy/launch
yetkilendirmesi; owner/legal/external aksiyonlar.

## Bu Fazı Bloke Eden Açık Ürün Kararları

`[DECISION REQUIRED — FATIH]`:

- Production domain.
- Support/sender adresleri.
- Ticari/hukuki metinler.
- Merchant/refund disclosure'ları.
- Kesin retention/RPO/RTO.
- OG personalization seviyesi.
- Launch zamanlaması.

Bu kararların büyük kısmı, ilgili milestone (P11-M1 iş/ticari hazırlık,
P11-M2 domain/DNS) başlamadan cevaplanmalıdır; launch zamanlaması yalnız
P11-M10'u (go-live) bloke eder.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | İş ve ticari hazırlık | Faz 10 | Fatih (owner), legal/commercial | Ticari olarak ücret kabul etmeye yetkili hesap ve imzalı/onaylı provider süreci var; credential'lar secret store'a teslim edilmiştir. |
| 2 | Domain, DNS, HTTPS ve public URL'ler | 1 | Fatih (owner), Backend, Security | Güvenilir HTTPS domain altında health ve public shell çalışır; düz HTTP auth/session trafiği kapalıdır. |
| 3 | Google OAuth production | 2 | Backend, Security | Google login gerçek production domaininde güvenli smoke geçer; raw IP deployment'ın yalnız email/password private acceptance olduğu açık kalır. |
| 4 | Ödeme production kurulumu | 1, 2 | Backend, Security | Test verisi temizlenmiş, ödeme/hak açma/audit evidence kayıtlı; hesap silme e-posta doğrulamasıyla anında tetiklenen Organization otomatik yenileme iptalinin gerçek sağlayıcıda çalıştığı ve doğrulandığı kanıtlı; Critical/High bulgu yok; kontrollü düşük tutarlı ilk gerçek ödeme Fatih onayıyla yapılır. |
| 5 | Resend production email | 1, 2 | Backend, Security | Production inbox'a teslim kanıtı; SPF/DKIM/DMARC sonucu ve alerting kaydı var. |
| 6 | Cloudflare/media production | 1, 2 | Fatih (owner), Backend, Security | Fatih Cloudflare hesabı/kaynakları ve hesaba özel limitleri hazırlar; scoped secret'lar secret store'a aktarılır; eşleşen exact HTTPS media CSP origin'leri API ve web image yapılandırmasına eklenir; EXIF temizliği, request/TUS byte-süre limitleri, expiry, private delivery, deletion/retry/cleanup ve residual exposure gerçek sağlayıcıda test edilip kabul edilir. Phase 4/5 local testleri production doğrulaması sayılmaz. |
| 7 | VPS, veritabanı ve operasyon | 2 | Backend, Security | Backup yalnız VPS'te değildir; restore timestamp/checksum/version/duration/query evidence var; rollback owner/runbook tanımlıdır; Lora'ya dokunulmamıştır. |
| 8 | Security go-live gate | 3-7 | Security, Reviewer | Phase 7 and Phase 10 deferred clean-checkout CI plus all release-candidate workflows pass in clean checkout; actual browser UI zoom at 200% and manual screen-reader/contrast review pass with evidence; no open Critical/High finding; every Medium has owner/risk/disposition; independent Security and Reviewer sign-off. Fatih deferred Git push and VPS deployment to Phase 11; neither is authorized before that phase is explicitly approved. |
| 9 | Hukuk, gizlilik, SEO ve public web | 1, 2 | Legal reviewer, Backend, Frontend | Legal owner onayı kayıtlıdır; public metadata yalnız accepted projection'dan gelir; share/QR management token/internal ID taşımaz. |
| 10 | Kontrollü production smoke ve go-live | 1–9 | Tester, Security, Fatih (go-live gate) | Bütün zorunlu maddeler PASS; Fatih trafik/duyuru başlangıcını açıkça onaylar; önce **PRODUCTION READY**, sonra ayrı owner kararıyla **LIVE**. |

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──┬──> M3 ──┐
            ├──> M4 ──┤
            ├──> M5 ──┼──> M8 ──┐
            ├──> M6 ──┤         │
            └──> M7 ──┘         ├──> M10
                                │
            M9 (M1, M2'ye bağımlı) ─┘
```

M3–M7, M2'den sonra farklı provider yüzeylerini (Google/ödeme/email/media/
VPS) etkilediği için paralel yürütülebilir; her biri kendi `[EXTERNAL
SETUP]`/`[EXTERNAL VERIFICATION REQUIRED]` ön koşuluna bağımlıdır. M8
(güvenlik kapısı) M3–M7'nin tamamına bağımlıdır. M10 (go-live) M8 ve M9'a
bağımlıdır.

## Beklenen Uzman Rolleri

- **Fatih (owner):** iş/ticari hesap sahipliği, domain/DNS sahipliği, ödeme
  go-live onayı, nihai go-live onayı.
- **Backend + Database:** domain/CORS/cookie config, provider entegrasyon
  kodu, VPS deploy/migration.
- **Security:** her provider entegrasyonu için signature/secret/fail-closed
  doğrulaması; güvenlik go-live kapısı.
- **Legal/commercial reviewer (Fatih/dış):** merchant, KVKK/gizlilik,
  ticari disclosure onayı.
- **Tester:** kontrollü production smoke, tüm akışların uçtan uca
  doğrulaması.
- **Reviewer:** güvenlik go-live kapısında bağımsız sign-off.

## Doğrulama Kriterleri

- Phase 0 threat register'ın bütün release gate'leri, gerçek provider/VPS
  konfigürasyonu ve release artifact'i üzerinde yeniden kanıtlanır; config
  inspection tek başına yeterli değildir.
- Kontrollü production E2E, provider failure/replay, all-resource
  authorization, actual browser UI 200% zoom and manual screen-reader/
  contrast review, abuse/load, migration/rollback, backup/
  restore, tarama ve monitoring/alerting evidence'ı toplanır.
- Her VPS işleminden sonra Lora'nın read-only health/resource kontrolü
  yapılır (`docs/DEPLOYMENT.md`).

## Faz Tamamlanma Kriterleri

- P11-M1–M10 tamamdır.
- Rollback yolu açıktır; monitoring aktiftir.
- External hesaplar Fatih'in kontrolündedir.
- Açık zorunlu gate yoktur.
- Engineering, Security, Tester, legal/commercial owner ve Fatih gate'leri
  kanıt bazlı kapatılmıştır.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; production URL'ler, release commit/image
digest'leri, migration ve smoke evidence, secret location adları (secret
değeri değil), provider/runbook sahipleri, rollback ve incident kontakları
günceller.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 11 uygulama izni değildir. Faz 11 — roadmap'teki
son faz — ancak Faz 1–10 fiilen tamamlanıp bağımsız doğrulandıktan **ve**
Fatih bu planı (veya doğrulanmış önceki fazlar sonrası güncellenmiş
halini) açıkça onayladıktan sonra başlayabilir. Bu fazın tamamlanması,
kontrollü gerçek kullanıcı ve ticari trafiği mümkün kılar. MVP launch
sonrası backlog/reliability roadmap'i yalnız yeni bir Fatih kararıyla
hazırlanır; bu roadmap otomatik olarak yeni bir faz başlatmaz.
