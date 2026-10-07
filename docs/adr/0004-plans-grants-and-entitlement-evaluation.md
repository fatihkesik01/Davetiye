# ADR-0004 — Planlar, Grant'ler ve Entitlement Değerlendirmesi

Durum: **Accepted; Phase 8 MVP commerce contract accepted 2026-10-06; legal/provider acceptance remains a Phase 11 gate**
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
- Numeric limit reductions do not delete data; read/delete/reducing operations continue, while new additions are blocked until usage is below the current limit. Boolean downgrade behavior follows accepted per-feature decisions in PRODUCT.md and the Phase 0 decision register.
- Organization subscription grant controls effective public access. At paid-through expiry, public access and active publication stop while invitation data remains preserved for a future eligible grant.
- Quota kontrolleri transaction-safe DB mutation ile korunur.
- Payment amount/currency/plan reference ödeme anında immutable snapshot
  olur.
- Plan/settings değişiklikleri audit edilir.
- Generic rule engine kurulmaz.

## Accepted Commerce Decisions and Remaining Phase 11 Gates

- Phase 8 MVP checkout/business behavior (PD-12), subscription expiry (PD-02), renewal, cancellation, refund flow, and related product decisions are recorded in `docs/PHASE_0_PLAN.md` section 12 and `docs/PHASE_8_PLAN.md`. Legal refund/tax wording and merchant-specific retry acceptance remain Phase 11 gates.

## Sonuçlar

- Super Admin ticari değerleri deploy olmadan değiştirebilir.
- Open product kararları ilgili business slice başlamadan kapanmalıdır;
  foundation schema bunlardan birini varsaymaz.
