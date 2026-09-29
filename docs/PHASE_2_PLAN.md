# Faz 2 — Davetiye Taslağı ve Şablon Kataloğu Planı

Durum: **Taslak — uygulanması onaylanmadı**
Bağımlılık: **Faz 1 bağımsız kapanış kapılarından geçmiş olmalı**

## Amaç

Bu faz, `docs/PRODUCT.md` §4–§7 ve kabul edilmiş ADR-0003/0007 ile uyumlu
olarak Creator'ın kendi hesabında bir davetiye taslağı oluşturmasını,
backend'e autosave edilmesini, şablon seçmesini ve aynı normalize render
modeliyle salt-okunur önizleme almasını sağlar. Üyelik gerektirmeyen şablon
kataloğu ve demo önizlemeleri de bu fazın parçasıdır. Faz, yayınlanmış
davetiye veya guest business akışını başlatmaz.

## Kapsam

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
- Required/recommended alan doğrulama/uyarı sözleşmesi, responsive ve
  erişilebilirlik testleri ile renderer görsel-regresyon yaklaşımı.
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
- Kullanıcı/Super Admin tarafından yazılan HTML/CSS/JavaScript template CMS'i
  (`docs/PRODUCT.md` §5 ve ADR-0007 gereği).
- Production deployment; Faz 1 Compose altyapısı bunun için bir temel olup
  canlı trafiğe yetki vermez.

## Bağımlılıklar

- Faz 1'in build, test, security, reviewer ve architecture kapanış
  kriterlerini geçmiş olması.
- ADR-0001 (modül sınırları), ADR-0002 (Creator ownership), ADR-0003
  (lifecycle/snapshot), ADR-0004 (entitlement), ADR-0005 (media boundary)
  ve ADR-0007 (renderer version contract) kabul kararları.
- Faz 1'in `EffectiveEntitlementResolver` öncesi yalnız schema sunduğu
  bilinmelidir; bu faz taslakta ticari entitlement tüketmez.

## Bu Fazı Bloke Eden Açık Ürün Kararları

Taslak, katalog ve preview milestone'ları için yeni bir ürün kararı gerekli
değildir. Aşağıdakiler ancak kapsam dışı lifecycle/public-yayın işi bu faza
sonradan eklenmek istenirse ilgili milestone'u bloke eder:

| Karar | Bloke ettiği olası iş |
| --- | --- |
| PD-01, PD-02, PD-03, PD-04, PD-05 | grant/entitlement ve active-invitation quota içeren yayın/plan işleri |
| PD-07, PD-13 | scheduled publish, cancel/reschedule ve scheduled autosave semantiği |
| PD-08 | trash restore hedef durumu |
| PD-09 | Expired → Scheduled geçişi |

Bu kararlar çözülmeden yayın lifecycle'ı için davranış varsayılmaz.

## Milestone'lar

| No | Milestone | Bağımlılık | Sorumlu roller | Tamamlanma ölçütü |
| ---: | --- | --- | --- | --- |
| 1 | Invitation/template contract ve migration tasarımı | Faz 1, ADR-0003/0007 | Architect, Backend, Database, Security | Tek sahipli şema, snapshot/template-version sınırları ve migration review kabulü; açık PD'ler davranışa çevrilmez. |
| 2 | TemplateDefinition katalog ve renderer registry | 1 | Backend, Database, Frontend, UI/UX | Idempotent seed, active/free-premium/module metadata ve `(templateKey, rendererVersion)` resolve testleri geçer. |
| 3 | Creator draft API ve autosave | 1 | Backend, Database, Security | Creator-owned CRUD, optimistic concurrency ve iki-Creator BOLA negatif integration testleri geçer. |
| 4 | Normalize render model ve başlangıç template seti | 2, 3 | Frontend, UI/UX | Yaklaşık sekiz code-owned renderer, demo/Creator preview parity ve visual-regression contract geçer. |
| 5 | Public katalog ve Creator draft editor UI | 2, 3, 4 | Frontend, UI/UX | `/sablonlar` demosu ve `/panel` taslak akışı responsive/a11y testleriyle çalışır; protected içerik guard dışına çıkmaz. |
| 6 | Required/recommended alan doğrulama ve güvenli audit/log | 3, 4 | Backend, Frontend, Security | Required alanlar API'de zorunlu, recommended alanlar explicit warning/override sözleşmeli; log/audit PII/secret sızdırmaz. |
| 7 | Contract, accessibility ve phase closure | 2–6 | Tester, Reviewer, Security, Architect | OpenAPI drift, unit/architecture/PostgreSQL integration, frontend/a11y/visual testleri yeşil; bağımsız gate'lerde Critical/High yok. |

## Milestone Bağımlılık Grafiği

```text
M1 ──> M2 ──> M4 ──> M5 ──> M7
 │       └──────────> M6 ──> M7
 └──> M3 ───────────> M5
                  └──> M6
```

M2 ve M3, M1'in kabul edilmiş contract'ından sonra farklı modül/dosya
alanlarında dikkatle paralel yürütülebilir. M4 renderer contract'ı ile M5
UI aynı frontend yüzeyini etkilediği için serialize edilir. M6, M3/M4
sözleşmeleri sabitlenmeden başlamaz.

## Beklenen Uzman Rolleri

- Architect: ADR-0003/0007 sınırları, snapshot/version ve ownership review.
- Database + Backend: schema/migration, transaction/concurrency, API ve
  authorization.
- Frontend + UI/UX: renderer'lar, katalog/editor, responsive/a11y ve
  preview parity.
- Security: BOLA, audit/log redaction, unsafe HTML/template sink ve
  ownership review.
- Tester: PostgreSQL integration, contract, browser accessibility ve visual
  regression kanıtı.
- Reviewer: faz sonunda kapsam/kalite bağımsız incelemesi.

## Doğrulama Kriterleri

- `dotnet build Davetiye.slnx --no-restore --nologo` sıfır hata/uyarıyla
  geçer.
- Architecture ve unit testleri; gerçek PostgreSQL Testcontainers integration
  testleri, özellikle foreign internal-ID read/mutation reddi, geçer.
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
- Tüm doğrulama kriterleri geçer; bağımsız Tester, Security, Reviewer ve
  Architect onayı vardır.
- Açık Critical/High bulgu yoktur. Medium bulguların sahibi ve çözüm/defer
  gerekçesi `AI_HANDOFF.md`'de yer alır.
- PD-01–PD-13'ten hiçbiri çözülmüş gibi işaretlenmez.

## Handoff Gereksinimleri

Faz sonunda `docs/AI_HANDOFF.md`; gerçek commit/branch durumu, her milestone
durumu, çalıştırılan komutların sonuçları, açık bulgular/teknik borç, PD
bağımlılıkları, renderer version migration notları ve sonraki önerilen işi
güncellemelidir.

## Sonraki Faz Planlama Kapısı

Bu taslağın varlığı Faz 2 uygulama izni değildir. Uygulama, kullanıcı bu
planı açıkça onayladıktan sonra başlar. Faz 2'nin tamamlanması da yeni bir
fazın otomatik başlamasına izin vermez.
