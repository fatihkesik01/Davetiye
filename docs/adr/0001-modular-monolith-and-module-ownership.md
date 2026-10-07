# ADR-0001 — Modular Monolith ve Modül Sahipliği

Durum: **Accepted (Kabul edildi)**  
Tarih: **2026-09-28**

## Bağlam

MVP tek ekip, tek deployment, ortak transactional kurallar ve sınırlı
operasyonel altyapı ile geliştirilecektir. Microservice sınırlarının
maliyetini haklı çıkaran bağımsız scale/deploy ihtiyacı yoktur.

## Karar

- Tek React uygulaması, tek ASP.NET Core process'i, tek PostgreSQL ve tek
  `DbContext` kullanılacak.
- Logical modüller: Identity & Accounts, Templates, Invitations, RSVP,
  Memories, Gift Registry, Media, Plans & Entitlements, Payments,
  Notifications, Administration ve Analytics.
- Her tablo bir modülün sahipliğinde olacak; başka modül doğrudan mutate
  etmeyecek.
- Cross-module davranış ID ve dar application-service sözleşmesiyle
  yürütülecek.
- Provider event'leri için ortak inbox/outbox persistence ve claim mekanikleri
  Integration Foundation tarafından sağlanacak; Payments ve Media kendi typed
  event handler'ını ve domain state geçişini sahiplenmeye devam edecek.
- Domain provider/framework bağımlılığı taşımayacak.
- Sunuma dönük değişken içerik JSONB olabilir; ownership ve işlem/state
  verisi relational olacak.
- Provider events inbox, güvenilir yan etkiler outbox ile işlenecek.
- API composition root, yalnız DI registration/wiring amacıyla Infrastructure'a
  referans verebilir; endpoint/business logic Infrastructure tiplerine
  bağlanamaz.

## Sonuçlar

- Tek transaction gerektiren invitation/entitlement/payment işlemleri
  basit kalır.
- Modül izolasyonu code review ve architecture tests ile korunmalıdır.
- Ayrı database, message broker veya distributed consistency eklenmez.
- Gelecekte ayrıştırma gerekirse ownership sınırları başlangıç noktasıdır;
  MVP bunun için ayrı deployable oluşturmaz.
