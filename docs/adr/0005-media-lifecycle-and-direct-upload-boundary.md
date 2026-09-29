# ADR-0005 — Media Lifecycle ve Direct Upload Trust Boundary

Durum: **Accepted**  
Tarih: **2026-09-28**

## Bağlam

Fotoğraf ve video byte'ları VPS/PostgreSQL'e alınmamalıdır. Browser
Cloudflare'a doğrudan yükler; ancak client metadata'sı ve “upload
tamamlandı” beyanı güvenilir değildir. Inactive invitation media'sı
kopyalanmış URL üzerinden açık kalmamalıdır.

## Karar

- Fotoğraf origin storage R2, delivery Images Transformations; video
  Stream kullanır.
- API actor/capability, invitation state, module, entitlement, hard
  ceiling, pending usage ve rate limit kontrolünden sonra private
  `PendingUpload` asset ve server-generated key oluşturur.
- Upload capability kısa ömürlü ve tek object/asset/method scope'ludur.
- Upload byte hard ceiling'i provider veya edge upload gateway tarafından
  upload sırasında uygulanır. Yalnız client `Content-Length` beyanına veya
  upload sonrası HEAD kontrolüne dayanan sınırsız presigned PUT kabul edilmez;
  aşım partial/multipart cleanup ve abuse metriği üretir.
- Cloudflare master credential browser'a verilmez.
- Client MIME/size/duration/completion authoritative değildir.
- R2 object server-side metadata/HEAD ve güvenli type doğrulamasından;
  Stream signed webhook/provider status doğrulamasından geçer.
- Lifecycle: PendingUpload → Processing → Ready veya Rejected;
  silmede PendingDeletion/Deleted.
- Yalnız Ready asset ve current invitation access gate delivery alır.
- R2 private, Stream playback signed/gated olur; kalıcı provider URL dönülmez.
  Görsel URL veya playback-session exchange capability'si current gate sonrası
  en fazla 60 saniyelik TTL ile üretilir. Stream playback session TTL'i izin
  verilen videoyu kesmeyecek kadar, fakat configurable ve hard-ceiling'li
  bounded bir süredir.
- Pause/expire/ban yeni issuance ve renewal'ı keser; mümkünse access revoke
  edilir fakat bytes silinmez. Trash delivery'yi kapatır, bytes retention
  boyunca kalır. Fiziksel provider silme yalnız permanent purge/account
  deletion sonrasında yapılır.
- Guest memory upload intent'i ilgili pending memory capability'sine
  bağlıdır.
- SVG/HTML/active content guest allowlist'inde değildir.
- Provider delete idempotent outbox/job ile retry edilir ve reconcile
  edilir.

## Sonuçlar

- Presigned URL bearer capability olarak korunmalıdır.
- Pending intents paralel quota bypass'ını önlemek için kullanıma dahil
  edilir.
- Public page state'i kapandığında yeni asset delivery capability'si
  üretilemediği; image/exchange TTL üst sınırı ve bounded Stream session'ın
  gerçek provider davranışı integration test ile doğrulanmalıdır.
