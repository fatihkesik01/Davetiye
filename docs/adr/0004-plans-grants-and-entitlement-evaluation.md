# ADR-0004 — Planlar, Grant'ler ve Entitlement Değerlendirmesi

Durum: **Accepted with open product decisions**  
Tarih: **2026-09-28**

## Bağlam

Ticari değerler deploy olmadan değişebilmelidir; güvenlik hard
ceiling'leri ve supported feature semantiği ise application tarafından
kontrol edilmelidir. Limit düşüşü mevcut veriyi silemez.

## Karar

- `Plan`, typed `EntitlementDefinition`, `PlanEntitlement`,
  `AccountPlanGrant` ve `PublicationWindow` ayrı kavramlardır.
- Supported key/type/validation ve hard ceiling kod sözleşmesidir; fiyat
  ve ticari değer DB'dedir.
- Free, one-time purchase ve Organization subscription aynı effective
  resolver altında farklı grant source'larıdır.
- Individual grant invitation'a atanır.
- Publication başlarken window/grant bağlantısı kalıcılaşır.
- Numeric limit düşüşü veri silmez; read/delete/azaltıcı işlem sürer, yeni
  ekleme kullanım numeric limit altına inene kadar engellenir. Boolean
  entitlement downgrade davranışı açık ürün kararıdır.
- Quota kontrolleri transaction-safe DB mutation ile korunur.
- Payment amount/currency/plan reference ödeme anında immutable snapshot
  olur.
- Plan/settings değişiklikleri audit edilir.
- Generic rule engine kurulmaz.

## Kabul Edilmemiş Açık Kararlar

- Free grant'in yenilenebilirliği
- Organization cancellation/renewal sonrası active window davranışı
- Başlamış window'a retroactive `maxPublishDays` düşüşü
- Boolean entitlement downgrade davranışı
- Scheduled/Paused kayıtların active quota hesabı
- Creator/Guest media quota scope'u

## Sonuçlar

- Super Admin ticari değerleri deploy olmadan değiştirebilir.
- Open product kararları ilgili business slice başlamadan kapanmalıdır;
  foundation schema bunlardan birini varsaymaz.
