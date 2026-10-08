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

Her kararın hangi sırada gerektiği aşağıdaki İş Takip Listesi'ndedir; launch
zamanlaması yalnız P11-M10'u (go-live) bloke eder.

## İş Takip Listesi (sıralı — Fatih buradan takip eder)

Fatih'in 2026-10-07 kararı: **şirket, vergi levhası, hukuk ve iyzico canlı
başvurusu en sona** bırakılır; önce şirket gerektirmeyen hazırlıklar yapılır.
Lora'ya (yedeği dahil) hiçbir şekilde dokunulmaz; yalnız VPS reboot'unun
Lora'da 1–3 dakikalık erişim kesintisi yaratması kabul edilmiştir.

**Kim:** 👤 Fatih · 🤖 AI agent · 🤝 Fatih bilgi/erişim verir, agent yapar.
**Durum:** ⬜ bekliyor · 🔄 devam ediyor · ✅ tamam · ⏸️ engelli.
Bir satır tamamlandığında agent durumu ve kısa notu burada günceller.

### Aşama 1 — Şirket gerektirmeyen hazırlıklar

| Sıra | İş | Kim | Önce bitmesi gereken | Milestone | Durum |
| ---: | --- | --- | --- | --- | --- |
| 1 | Phase 11'i açıkça onayla | 👤 | — | — | ✅ 2026-10-08 Fatih Phase 11'i ve deploy'u onayladı |
| 2 | VPS bakım kararı: reboot saati, SSH şifreli girişin kapatılması, bekleyen güvenlik güncellemeleri, kullanılmayan 8080/5050 ufw kuralları | 👤 karar → 🤖 | 1 | M7 | ⬜ |
| 3 | Davetiye'yi VPS'e IP üzerinden deploy et (özel smoke; gerçek kullanıcı trafiği yok) | 🤖 (Fatih "deploy et" onayı) | 1 | M7 | ✅ 2026-10-08 `/opt/davetiye`, commit `51af55f`; api/web/postgres healthy, migration'lar uygulandı; Lora etkilenmedi |
| 4 | Organization abonelik iptali için iyzico adapter/worker kodu | 🤖 | 1 | M4 | ⬜ |
| 5 | Kalan Low teknik borçlar (trusted-proxy CIDR, session/draft rate-limit vb.) | 🤖 | 1 | M8 | ⬜ |
| 6 | Domain adını seç ve satın al | 👤 | — | M2 | ✅ 2026-10-07 `kutlio.com` (Cloudflare Registrar, bitiş 2027-10-07) |
| 7 | Cloudflare hesabı aç, domain'i ekle, nameserver'ları Cloudflare'e yönlendir | 👤 | 6 | M2 | ✅ 2026-10-07 zone Active, Cloudflare nameserver'ları |
| 8 | DNS kayıtları (A → VPS IP, www) | 🤝 | 3, 7 | M2 | ✅ 2026-10-08 `kutlio.com` ve `www` A → 187.77.92.30 (DNS only) |
| 9 | Nginx sitesi + HTTPS (Let's Encrypt), production URL/CORS/cookie ayarları | 🤖 | 3, 8 | M2 | ✅ 2026-10-08 nginx site `/etc/nginx/sites-available/kutlio.com`, Let's Encrypt (bitiş 2027-01-05, otomatik yenileme), HTTP→HTTPS 301, HSTS. Açık: `/` için ana sayfa ürün kararı bekliyor |
| 10 | Destek/gönderici adreslerini belirle (`destek@`, `noreply@`) ve posta kutusu çözümü seç (ör. Cloudflare Email Routing veya Zoho/Google Workspace) | 👤 | 7 | M5 | ✅ 2026-10-07 Cloudflare Email Routing: `destek@` ve `noreply@kutlio.com` → 01fatihkesik@gmail.com; diğerleri Drop |
| 11 | Resend hesabı aç, domain'i ekle | 👤 | 7 | M5 | ✅ 2026-10-07 Resend hesabı (01fatihkesik), `kutlio.com` eklendi, bölge Ireland (eu-west-1), tracking kapalı |
| 12 | SPF/DKIM/DMARC kayıtları, Resend production anahtarı, teslim testi | 🤝 | 9, 10, 11 | M5 | ✅ 2026-10-08 Resend `kutlio.com` Verified (EU); Sending-only anahtar sunucu `.env`'inde (Fatih girdi, `/opt/davetiye/set-secret.sh`) |
| 13 | Google Cloud projesi + OAuth onay ekranı + domain doğrulama (Search Console) | 👤 | 9 | M3 | ⬜ |
| 14 | Google OAuth production ayarı ve smoke testi | 🤖 | 13 | M3 | ⬜ |
| 15 | Cloudflare R2 / Images / Stream'i etkinleştir (kart gerekir), hesaba özel limitleri incele | 👤 (limit incelemesi 🤝) | 7 | M6 | ⬜ |
| 16 | Dar yetkili Cloudflare token'ları, media Worker deploy, medya CSP origin'leri | 🤖 | 9, 15 | M6 | ⬜ |
| 17 | Medyanın gerçek sağlayıcıda kabulü (yükleme, EXIF temizliği, limitler, süre dolumu, silme/retry, yükleme sırasında süre dolması) | 🤖 | 16 | M6 | ⬜ |
| 18 | Off-site yedek hedefini seç (ör. ayrı R2 bucket'ı veya Backblaze) | 👤 | — | M7 | ⬜ |
| 19 | Günlük şifreli Davetiye yedeği + restore tatbikatı + rollback runbook | 🤖 | 3, 18 | M7 | ⬜ |
| 20 | İzleme/uyarı (uptime, hata, disk) ve uyarıların gideceği adres | 🤖 + 👤 adres | 9 | M7 | ⬜ |
| 21 | iyzico sandbox hesabı (ücretsiz, şirket gerektirmez) | 👤 | — | M4 | ⬜ |
| 22 | iyzico sandbox ile ödeme, webhook ve abonelik iptali uçtan uca testleri | 🤖 | 4, 9, 21 | M4 | ⬜ |
| 23 | Gerçek tarayıcı %200 zoom (otomatik kısım) | 🤖 | 9 | M8 | ⬜ |
| 24 | Ekran okuyucu + kontrast elle test turu | 👤 (veya tester) | 9 | M8 | ⬜ |
| 25 | OG/paylaşım önizlemesi kişiselleştirme seviyesi kararı | 👤 karar → 🤖 | — | M9 | ⬜ |
| 25a | **Marka adı "Kutlio"** (Fatih kararı 2026-10-08): uygulama başlığı/logo metni, sayfa başlıkları, e-posta gönderen adı (`RESEND_FROM_NAME`) ve ürün dokümanları | 👤 karar ✅ → 🤖 | — | M9 | ✅ 2026-10-08 commit `a90e56d` deploy edildi: başlıklar, logo metni, gizlilik/hizmet metinleri, ICS, e-posta gönderen adı "Kutlio" |
| 25b | **Gerçek ana sayfa (`/`)** — ürünü anlatan tanıtım sayfası (Fatih kararı 2026-10-08). Önce içerik/bölümler birlikte kararlaştırılır ve `docs/PRODUCT.md`'ye eklenir, sonra tasarım + uygulama. Şu an `/` "Sayfa bulunamadı" gösteriyor | 👤 içerik kararı → 🤖 | 25a | M9 | 🔄 kod hazır: `GET /api/v1/public/plans` + `/` tanıtım sayfası; Security ACCEPT (M1 cache düzeltildi), Tester/Reviewer ACCEPT. Deploy için: CI integration yeşil + Fatih SSS onayı. Açık Low: anonim yanıtta `maxImages`/`maxVideos`, eksik entitlement'lı planın log'suz gizlenmesi |
| 25c | Resend doğrulamasını kontrol et, gönderme-yetkili API anahtarını sunucu `.env`'ine ekle, kayıt doğrulama e-postasını uçtan uca test et | 🤖 | 12 | M5 | ✅ 2026-10-08 kayıt doğrulama e-postası Resend'de `delivered`, gönderen Kutlio <noreply@kutlio.com> |

### Aşama 2 — Şirket, hukuk ve ödeme (en son)

| Sıra | İş | Kim | Önce bitmesi gereken | Milestone | Durum |
| ---: | --- | --- | --- | --- | --- |
| 26 | Şirket aç (şahıs şirketi), vergi levhası, ticari banka hesabı | 👤 (mali müşavir) | — | M1 | ⬜ |
| 27 | ETBİS ve VERBİS gerekliliğini doğrula, gerekiyorsa kaydol | 👤 (mali müşavir/hukukçu) `[EXTERNAL VERIFICATION REQUIRED]` | 26 | M1/M9 | ⬜ |
| 28 | Hukuki metinler: KVKK aydınlatma, gizlilik, çerez, kullanım şartları, mesafeli satış, ön bilgilendirme, iptal/iade, fiyat/vergi gösterimi | 👤 hukukçu onayı → 🤖 siteye ekler | 26 | M9 | ⬜ |
| 29 | Kesin saklama süreleri (audit, ödeme, silinen hesap, tombstone ID) | 👤 hukukçu → 🤖 uygular | 28 | M9 | ⬜ |
| 30 | iyzico canlı üye işyeri başvurusu ve onayı | 👤 | 9, 26, 28 | M1/M4 | ⬜ |
| 31 | iyzico canlı anahtarları, webhook imzası, iade/itiraz/chargeback kaynağı doğrulaması | 🤖 | 22, 30 | M4 | ⬜ |
| 32 | Security go-live incelemesi ve release commit'te temiz CI (Security + Reviewer) | 🤖 | 2–31 | M8 | ⬜ |
| 33 | Kontrollü düşük tutarlı ilk gerçek ödeme | 👤 onay → 🤖 | 31, 32 | M4 | ⬜ |
| 34 | Uçtan uca production smoke testi → **PRODUCTION READY** | 🤖 | 33 | M10 | ⬜ |
| 35 | Go-live kararı (trafik/duyuru) → **LIVE** | 👤 | 34 | M10 | ⬜ |

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | İş ve ticari hazırlık (en son) | Faz 10 | Fatih (owner), legal/commercial | Ticari olarak ücret kabul etmeye yetkili hesap ve imzalı/onaylı provider süreci var; credential'lar secret store'a teslim edilmiştir. |
| 2 | Domain, DNS, HTTPS ve public URL'ler | — | Fatih (owner), Backend, Security | Güvenilir HTTPS domain altında health ve public shell çalışır; düz HTTP auth/session trafiği kapalıdır. |
| 3 | Google OAuth production | 2 | Backend, Security | Google login gerçek production domaininde güvenli smoke geçer; raw IP deployment'ın yalnız email/password private acceptance olduğu açık kalır. |
| 4 | Ödeme production kurulumu | 1, 2 | Backend, Security | Test verisi temizlenmiş, ödeme/hak açma/audit evidence kayıtlı; hesap silme e-posta doğrulamasıyla anında tetiklenen Organization otomatik yenileme iptalinin gerçek sağlayıcıda çalıştığı ve doğrulandığı kanıtlı; Critical/High bulgu yok; kontrollü düşük tutarlı ilk gerçek ödeme Fatih onayıyla yapılır. |
| 5 | Resend production email | 2 | Backend, Security | Production inbox'a teslim kanıtı; SPF/DKIM/DMARC sonucu ve alerting kaydı var. |
| 6 | Cloudflare/media production | 2 | Fatih (owner), Backend, Security | Fatih Cloudflare hesabı/kaynakları ve hesaba özel limitleri hazırlar; scoped secret'lar secret store'a aktarılır; eşleşen exact HTTPS media CSP origin'leri API ve web image yapılandırmasına eklenir; EXIF temizliği, request/TUS byte-süre limitleri, expiry, private delivery, deletion/retry/cleanup ve residual exposure gerçek sağlayıcıda test edilip kabul edilir. Phase 4/5 local testleri production doğrulaması sayılmaz. |
| 7 | VPS, veritabanı ve operasyon | 2 | Backend, Security | Backup yalnız VPS'te değildir; restore timestamp/checksum/version/duration/query evidence var; rollback owner/runbook tanımlıdır; Lora'ya dokunulmamıştır. |
| 8 | Security go-live gate | 3-7 | Security, Reviewer | All release-candidate workflows pass in clean checkout on the release commit (Phase 2–10 clean CI first passed 2026-10-07, run 37653912659); actual browser UI zoom at 200% and manual screen-reader/contrast review pass with evidence; no open Critical/High finding; every Medium has owner/risk/disposition; independent Security and Reviewer sign-off. VPS deployment is not authorized before Phase 11 is explicitly approved. |
| 9 | Hukuk, gizlilik, SEO ve public web | 1, 2 | Legal reviewer, Backend, Frontend | Legal owner onayı kayıtlıdır; public metadata yalnız accepted projection'dan gelir; share/QR management token/internal ID taşımaz. |
| 10 | Kontrollü production smoke ve go-live | 1–9 | Tester, Security, Fatih (go-live gate) | Bütün zorunlu maddeler PASS; Fatih trafik/duyuru başlangıcını açıkça onaylar; önce **PRODUCTION READY**, sonra ayrı owner kararıyla **LIVE**. |

## Milestone Bağımlılık Grafiği

```text
        ┌──> M3 ──┐
        ├──> M5 ──┤
M2 ─────┼──> M6 ──┤
        ├──> M7 ──┤
        └─────────┼──> M4 (sandbox) ─┐
                  │                  │
M1 (şirket/vergi, en son) ──> M9 ──> M4 (canlı) ──> M8 ──> M10
```

M3, M5, M6 ve M7 domain/HTTPS (M2) sonrasında paralel yürütülebilir. M4'ün
sandbox kısmı şirket beklemez; canlı kısmı M1 (şirket/vergi) ve M9 (hukuki
metinler) sonrasına kalır. M8 güvenlik kapısı ve M10 go-live en sondadır.

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
