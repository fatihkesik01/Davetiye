# ADR-0006 — Payment Webhook ve Idempotent Entitlement Activation

Durum: **Accepted**  
Tarih: **2026-09-28**

## Bağlam

Production payment provider iyzico'dur. Browser redirect'i taklit
edilebilir, provider webhook'u tekrar veya sıra dışı gelebilir. Tek
ödeme birden fazla entitlement üretmemelidir.

## Karar

- Checkout plan/amount/TRY currency server DB'sinden okunur.
- API immutable PaymentAttempt ve internal correlation/reference üretir.
- Browser success/return entitlement açmaz.
- Webhook raw request kullanılan iyzico merchant/API sürümünün resmi
  signature sözleşmesiyle doğrulanır.
- Provider event/payment reference durable WebhookInbox ve unique
  constraint ile idempotent olur.
- Amount, currency, merchant/reference, account, plan ve provider state
  server kaydıyla reconcile edilir.
- Inbox processed, payment transition ve AccountPlanGrant aynı DB
  transaction'ında deterministic unique grant key ile gerçekleşir.
- Email/yan etkiler outbox'tan işlenir.
- Out-of-order event state regression oluşturmaz.
- FakePaymentGateway production config'de startup failure üretir.
- Card data, secret ve gereksiz raw provider payload tutulmaz/loglanmaz.

## Sonuçlar

- Redirect UX için kullanılabilir fakat ödeme kanıtı değildir.
- iyzico sandbox signature fixture'ları gerçek entegrasyondan önce
  zorunlu integration gate'idir.
- Retry ikinci payment/grant üretmez.

