# Faz 2 — Davetiye Taslağı ve Şablon Kataloğu Planı

Durum: **Tamamlandı ve bağımsız doğrulandı — 2026-10-01**
Bağımlılık: **Faz 1 bağımsız kapanış kapılarından geçmiş olmalı**

## Amaç

Bu faz, `docs/PRODUCT.md` §4–§7 ve kabul edilmiş ADR-0003/0007 ile uyumlu
olarak Creator'ın kendi hesabında bir davetiye taslağı oluşturmasını,
backend'e autosave edilmesini, şablon seçmesini ve aynı normalize render
modeliyle salt-okunur önizleme almasını sağlar. Üyelik gerektirmeyen şablon
kataloğu ve demo önizlemeleri de bu fazın parçasıdır. Faz, yayınlanmış
davetiye veya guest business akışını başlatmaz.

Repository'de backend auth foundation bulunmasına rağmen frontend şu anda
yalnız feature'sız protected shell içerir. Bu nedenle Creator draft akışının
gerçek kullanıcı tarafından kullanılabilmesi için register/login/email
verification/password-reset/session/logout UI'si ve güvenli return flow bu
fazın ön bağımlı milestone'udur. Mevcut backend registration/Google yolu yalnız
Individual hesap üretir; PRODUCT'taki Organization account onboarding'i de
bu milestone'da, Fatih'in 2026-09-29'da kabul ettiği account-type UX kararıyla
(kayıtta seçim, sonradan dönüşüm yok) tamamlanır. Production Google OAuth domain + HTTPS
gereksinimi Faz 2 kapsamında değildir; development/localhost ve fail-closed
production-disabled davranışı doğrulanır.

## Kapsam

- Mevcut backend auth sözleşmesini kullanan gerçek Creator onboarding UI'si:
  register, login, email verification, password reset, session/logout ve
  güvenli application-local return flow; kabul edilen davranışa göre Individual
  ve MVP single-owner Organization account creation/onboarding.
- `Invitations` modülünde Creator-owned Invitation, current WorkingContent,
  template pin (`templateKey`, `rendererVersion`) ve taslak read/write
  modelinin migration, domain ve API katmanları.
- Sadece Creator'ın kendi taslağını listeleme, oluşturma, okuma ve
  güncelleme yetkileri; iki Creator arasında BOLA negatif testleri.
- Backend otoritesinde autosave ve concurrency/revision davranışı; browser
  storage yalnız bağlantı kopması kurtarma desteği olarak ele alınır,
  otoriter kayıt değildir.
- `TemplateDefinition` metadata modeli, code-owned renderer registry,
  idempotent katalog seed'i ve yaklaşık sekiz başlangıç şablonunun güvenli
  React renderer'ları.
- Public `/sablonlar` katalog/demoları ve Creator `/panel` taslak akışı;
  Creator preview ve demo preview aynı normalized render modelini kullanır.
- Incomplete Draft kaydını engellemeyen required/recommended validation-report
  sözleşmesi, responsive ve erişilebilirlik testleri ile renderer
  görsel-regresyon yaklaşımı. Publish blocker/warning override davranışı bu
  fazda uygulanmaz.
- Audit/structured log'a taslak komutlarının güvenli, PII/secret içermeyen
  olay kaydı ve OpenAPI-generated client güncellemesi.

## Kapsam Dışı

- Yayınla, schedule, pause, expire, restore, trash/purge veya gerçek public
  invitation URL'si; bunlar lifecycle/product kararlarına bağlı sonraki
  faz işidir.
- RSVP, Memories, Gift, view statistics, guest mutation veya public
  paylaşım/QR davranışı.
- Cloudflare R2/Images/Stream upload ve media asset yönetimi.
- iyzico checkout/webhook, gerçek Resend gönderimi, plan/grant verme veya
  kapsamlı Super Admin yönetimi.
- PRODUCT §19–§21 başlangıç Plan/Entitlement/SystemSetting seed'leri; bunlar
  entitlement/publication phase'inde idempotent DB seed olarak ele alınır.
- Kullanıcı/Super Admin tarafından yazılan HTML/CSS/JavaScript template CMS'i
  (`docs/PRODUCT.md` §5 ve ADR-0007 gereği).
- Production deployment; Faz 1 Compose altyapısı bunun için bir temel olup
  canlı trafiğe yetki vermez.
- Production Google OAuth enablement ve gerçek domain/HTTPS kurulumu.

## Bağımlılıklar

- Faz 1'in build, test, security, reviewer ve architecture kapanış
  kriterlerini geçmiş olması.
- ADR-0001 (modül sınırları), ADR-0002 (Creator ownership), ADR-0003
  (lifecycle/snapshot), ADR-0004 (entitlement), ADR-0005 (media boundary)
  ve ADR-0007 (renderer version contract) kabul kararları.
- Faz 1'in `EffectiveEntitlementResolver` öncesi yalnız schema sunduğu
  bilinmelidir; bu faz taslakta ticari entitlement tüketmez.
- Faz 1 backend auth endpoint'leri ve feature'sız route guard shell'i vardır;
  Creator onboarding business UI'si henüz yoktur ve bu planın M1'inde
  tamamlanmalıdır.

## Bu Fazı Bloke Eden Açık Ürün Kararları

PD-01–PD-13 içinden hiçbiri core Draft schema/autosave milestone'unu bloke
etmez. Aşağıdakiler ancak kapsam dışı lifecycle/public-yayın işi bu faza
sonradan eklenmek istenirse ilgili milestone'u bloke eder:

| Karar | Bloke ettiği olası iş |
| --- | --- |
| PD-01, PD-02, PD-03, PD-04, PD-05 | grant/entitlement ve active-invitation quota içeren yayın/plan işleri |
| PD-07, PD-13 | scheduled publish, cancel/reschedule ve scheduled autosave semantiği |
| PD-08 | trash restore hedef durumu |
| PD-09 | Expired → Scheduled geçişi |

Bu kararlar çözülmeden yayın lifecycle'ı için davranış varsayılmaz.

Aşağıdaki kararlar **Fatih tarafından 2026-09-29'da cevaplanmıştır** (bkz.
`docs/ROADMAP.md` §4a):

- **Organization account type:** Kayıt sırasında Individual/Organization
  açıkça seçilir; Individual hesap sonradan Organization'a **dönüştürülemez**.
- **Google same-email conflict:** 409 ile sessiz reddetme değil, **explicit
  account-linking UX** eklenir. Bu, M1'de ek bir security/architecture
  review'ı zorunlu kılar: linking eklenmeden önce
  `GoogleSignInService.CompleteSignInAsync`'in `bypassTwoFactor: true`
  çağrısının dayandığı "Google-linked identity Super Admin olamaz"
  invariant'ı kod-enforced hale getirilip gerçek testle kanıtlanmalıdır (bkz.
  `docs/PHASE_1_PLAN.md` M6b Low technical debt notu).
- **Premium template seçimi:** Hak sahibi olmayan Creator, premium template'i
  Draft/preview aşamasında **seçebilir**; yalnız publish preflight bu durumda
  blocker olarak engeller.
- **Wizard adım sırası:** `docs/PRODUCT.md` §4'teki örnek akış **kesin sıra
  olarak kabul edildi (2026-09-29):** Etkinlik türü → Temel bilgiler →
  Şablon → Tarih/Mekan → İsteğe bağlı bölümler → Önizleme → Yayınla.
- **Başlangıç event-type/taxonomy etiketleri:** Türkiye pazarına uygun
  standart set **kabul edildi (2026-09-29):** Düğün, Nişan, Kına, Sünnet,
  Doğum Günü, Baby Shower, Mezuniyet, Açılış/Genel — sekiz başlangıç
  template'iyle eşleşecek şekilde.

M6'yı bloke eden açık karar kalmadı; M6 artık yalnız M1/M3/M4/M5'in
tamamlanmasına bağımlıdır.

İlk yaklaşık sekiz template'in brief, kategori ve demo içerik seti
`docs/PRODUCT.md` §5 gereği UI/UX + Frontend specialist-owned implementation
input'udur; yukarıdaki sekiz event-type etiketiyle hizalanır. Fatih creative
direction verebilir; bu mandatory product-decision gate değildir.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü | Durum |
| ---: | --- | --- | --- | --- | --- |
| 1 | Creator auth/account-type onboarding frontend + Google account linking | Faz 1 auth foundation; Organization/linking kararları accepted (2026-09-29) | Frontend, Backend, UI/UX, Security | Kabul edilmiş Individual/Organization creation/onboarding (dönüşüm yok), login/verify/reset/session/logout, safe return flow ve explicit Google account-linking UX responsive/a11y/integration testleriyle çalışır; protected content flash veya browser token storage yoktur; linking'den önce "Google-linked identity Super Admin olamaz" invariant'ı kod-enforced hale getirilip test edilmiştir. | **Tamamlandı, doğrulandı.** Backend ve frontend auth/onboarding/linking akışları; token URL temizliği; desktop/320px/200% zoom browser, klavye ve axe kontrolleri geçti. Tester, Reviewer/UI-UX ve Security kabul verdi; açık Critical/High/Medium yok. |
| 2 | Invitation/template contract ve migration tasarımı | Faz 1, ADR-0003/0007 | Architect, Backend, Database, Security | Tek sahipli şema ve migration review kabulü; incomplete Draft required alan eksikleriyle kaydedilebilir; açık PD'ler davranışa çevrilmez. | **Tamamlandı, doğrulandı** (Database: Invitation/WorkingContent/TemplateDefinition şeması + migration, 114/66/58 test yeşil; Architect review: 0 Critical/High/Medium, `CurrentRendererVersion` kolonu keep-with-invariant kararı M4'e bağlayıcı not olarak kaydedildi, 1 Low dokümantasyon notu işlendi). |
| 3 | Creator draft API ve autosave | 2 | Backend, Database, Security | Creator-owned CRUD, optimistic concurrency ve iki-Creator BOLA negatif integration testleri geçer. | **Tamamlandı, doğrulandı.** Creator-owned CRUD/autosave, optimistic concurrency, CSRF, pagination validation, OpenAPI/generated client ve iki-Creator BOLA gerçek PostgreSQL testleri geçti. Tester, Reviewer ve Security kabul verdi. |
| 4 | TemplateDefinition katalog ve renderer registry | 2 ve premium Draft-selection kararı | Backend, Database, Frontend, UI/UX | Idempotent seed, active/free-premium/module metadata ve `(templateKey, rendererVersion)` resolve testleri geçer. | **Tamamlandı, doğrulandı.** Sekiz kayıtlık katalog ve gerçek preview asset'leri, migration-sonrası idempotent seed, inactive filtreleme, historical renderer sürüm listesi ve backend–React manifest drift kapısı geçti. Tester, Reviewer/UI-UX ve Security kabul verdi. |
| 5 | Normalize render model ve başlangıç template seti | 3, 4 ve specialist-owned reviewed design brief | Frontend, UI/UX | Yaklaşık sekiz code-owned renderer, demo/Creator preview parity ve visual-regression contract geçer. | **Tamamlandı, doğrulandı.** Normalize model, sekiz code-owned renderer, demo/Creator parity, DOM snapshot'ları ve 24 gerçek Chromium pixel baseline'ı; desktop/320px/200% zoom axe/overflow kontrolleri geçti. Tester, Reviewer/UI-UX ve Security kabul verdi. |
| 6 | Public katalog ve structured Creator draft editor/preview UI | 1, 3, 4, 5 ve ilgili UI kararları | Frontend, UI/UX | `/sablonlar` demosu ve `/panel` taslak akışı responsive/a11y testleriyle çalışır; protected içerik guard dışına çıkmaz. | **Tamamlandı, doğrulandı.** Public katalog/demo; protected dashboard/create/edit akışı; kesin yedi adımlı wizard; autosave/recovery/conflict; telefon/tablet/masaüstü ve protected yeni-sekme preview tamamlandı. Seçili katalog şablonu yavaş/hatalı çözümlemede sessizce kaybolmaz; `programItems` ve mevcut `timeZoneId` kayıplı autosave'e uğramaz. |
| 7 | Required/recommended validation report ve güvenli audit/log | 3, 5 | Backend, Frontend, Security | Incomplete Draft save edilir; required/recommended sonuçları gelecek publish preflight için raporlanır fakat publish/override eylemi eklenmez; log/audit PII/secret sızdırmaz. | **Tamamlandı, doğrulandı.** Owner-scoped/no-store validation raporu, template/alan fail-closed sonuçları, UI özeti ve PII/body/secret içermeyen structured loglar tamamlandı; publish/override eklenmedi. İki-Creator BOLA ve inactive/unknown-field testleri geçti. |
| 8 | Contract, accessibility ve phase closure | 1–7 | Tester, Reviewer, Security, Architect | OpenAPI drift, unit/architecture/PostgreSQL integration, auth/frontend/a11y/visual testleri yeşil; bağımsız gate'lerde Critical/High yok. | **Tamamlandı, doğrulandı.** Canlı OpenAPI sync/verify/check, build/test/a11y/visual kapıları geçti. Tester+Reviewer, Security ve Architect ACCEPT verdi; açık Critical/High/Medium yok. |

Bağımsız doğrulamayla tamamlanmış Phase 2 milestone oranı **8/8 (%100)**'dür.
M1–M8 tamamlandı; Phase 2, 2026-10-01 tarihinde bağımsız kapanış
kapılarından geçerek tamamlandı. Phase 3 ayrıca açık kullanıcı onayı olmadan
uygulanamaz.

## Milestone Bağımlılık Grafiği

```text
M1 ───────────────────────────────> M6 ──> M8
M2 ──> M3 ────────────────┬──────> M6
 └──> M4 ──> M5 ─────────┘
       │      └───────────────────> M7 ──> M8
       └──────────────────────────> M6
```

M1 auth UI ve M2 data-contract işi farklı alanlarda dikkatle paralel
yürütülebilir. M3 ve M4, M2'nin kabul edilmiş contract'ından sonra
paralelleşebilir. M5 renderer ile M6 editor/preview aynı frontend yüzeyini
etkilediği için serialize edilir. M7, M3/M5 sözleşmeleri sabitlenmeden başlamaz.

## Beklenen Uzman Rolleri

- Architect: ADR-0003/0007 sınırları, snapshot/version ve ownership review.
- Database + Backend: schema/migration, transaction/concurrency, API ve
  authorization.
- Frontend + UI/UX: auth/onboarding, renderer'lar, katalog/editor,
  responsive/a11y ve preview parity.
- Security: BOLA, audit/log redaction, unsafe HTML/template sink ve
  ownership review.
- Tester: PostgreSQL integration, contract, browser accessibility ve visual
  regression kanıtı.
- Reviewer: faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture testleri ve gerçek PostgreSQL Testcontainers integration
  testleri, özellikle foreign internal-ID read/mutation reddi, geçer.
- Saf unit test kapsamı zorunlu değildir (Fatih'in kararı: manuel/UI testi
  tercih edilir); izole edilmesi güç, kritik mantık için (örn. hard-ceiling/
  value-type/concurrency guard'ları) hedefli unit test eklenebilir ama şart
  değildir.
- Register/login/verification/reset/session/logout frontend-backend integration,
  safe-return ve protected-content-no-flash testleri geçer.
- Güncel canlı backend OpenAPI belgesine karşı
  `npm run api:sync`, `api:verify` ve `api:check` geçer.
- `npm run lint`, `npm run typecheck`, `npm test -- --run` ve production
  build geçer.
- 320 CSS px, 200% zoom, keyboard-only ve automated-a11y smoke; renderer
  visual-regression kanıtı uygulanır.
- CI clean checkout'ta bu kontrolleri ve container smoke'u tekrarlar.

## Faz Tamamlanma Kriterleri

- Taslak/katalog/preview kapsamı PRODUCT, UX_FLOWS ve ADR-0003/0007 ile
  karşılaştırılmış ve kapsam dışı public-yayın/guest/media/commerce işi
  eklenmemiştir.
- Incomplete Draft required alan eksikleriyle kaydedilebilir; validation report
  Phase 3 publish preflight'ine veri sağlar fakat bu faz publish/override
  davranışı üretmez.
- Kabul edilmiş Individual ve MVP single-owner Organization onboarding yolu
  gerçek UI/API testleriyle çalışır; conversion/linking davranışı varsayılmaz.
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer ve
  Architect onayı vardır.
- Açık Critical/High bulgu yoktur. Medium bulguların sahibi ve çözüm/defer
  gerekçesi `AI_HANDOFF.md`'de yer alır.
- PD-01–PD-13'ten hiçbiri çözülmüş gibi işaretlenmez.
- Faz 2 UI/template milestone'larını bloke eden roadmap product kararları
  Fatih tarafından açıkça cevaplanmış veya ilgili milestone başlanmamış olur.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; gerçek commit/branch durumu, her milestone
durumu, çalıştırılan komutların sonuçları, açık bulgular/teknik borç, PD
bağımlılıkları, renderer version migration notları ve sonraki önerilen işi
güncellemelidir.

## Sonraki Faz Planlama Kapısı

Fatih bu planı **2026-09-29'da açıkça onayladı**; Faz 2 implementasyonu
başlayabilir. Faz 2'nin tamamlanması yine de yeni bir fazın (Faz 3) otomatik
başlamasına izin vermez — Faz 3, Faz 2 bağımsız doğrulandıktan **ve** ayrıca
onaylandıktan sonra başlar.
