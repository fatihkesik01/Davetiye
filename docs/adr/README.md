# Architecture Decision Index

`docs/PRODUCT.md` product source of truth'tür. Bu klasördeki ADR'lar
yalnız kabul edilmiş teknik kararları kaydeder. Ürün kararı bekleyen
noktalar `docs/PHASE_0_PLAN.md` içindeki decision register'dadır ve
onay verilmeden implement edilmez.

| ADR | Durum | Konu |
| --- | --- | --- |
| [ADR-0001](0001-modular-monolith-and-module-ownership.md) | Accepted | Modular monolith ve modül sahipliği |
| [ADR-0002](0002-identity-authorization-publiccode-and-guest-capabilities.md) | Accepted | Identity, authorization, publicCode ve guest capability'leri |
| [ADR-0003](0003-invitation-lifecycle-content-snapshots-and-trash.md) | Accepted; açık kararlar ayrı | Lifecycle, content snapshot ve trash |
| [ADR-0004](0004-plans-grants-and-entitlement-evaluation.md) | Accepted; açık kararlar ayrı | Plan, grant ve entitlement değerlendirmesi |
| [ADR-0005](0005-media-lifecycle-and-direct-upload-boundary.md) | Accepted | Media lifecycle ve direct upload boundary |
| [ADR-0006](0006-payment-webhook-and-idempotent-entitlement-activation.md) | Accepted | Payment webhook ve idempotent grant |
| [ADR-0007](0007-template-renderer-version-contract.md) | Accepted | Template renderer version sözleşmesi |
