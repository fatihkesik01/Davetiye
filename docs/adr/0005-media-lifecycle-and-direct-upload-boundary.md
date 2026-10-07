# ADR-0005 — Media Lifecycle ve Direct Upload Trust Boundary

Durum: **Accepted (technical boundary clarification 2026-10-03)**
İlk kabul: **2026-09-28**

## Bağlam

Fotoğraf ve video byte'ları VPS/PostgreSQL'e alınmamalıdır. Browser
Cloudflare'a doğrudan yükler; ancak Creator fotoğraflarının EXIF/GPS
bilgisi provider storage'a yazılmadan önce kaldırılmalı, client metadata'sı
ve “upload tamamlandı” beyanı güvenilir sayılmamalıdır. Inactive invitation
media'sı kopyalanmış URL üzerinden açık kalmamalıdır.

## Karar

- Fotoğraf origin storage private R2, delivery Images Transformations;
  video Stream kullanır.
- Creator fotoğrafı alımı, sağlayıcıya ait edge giriş noktası olarak bir
  Cloudflare Worker kullanır: tarayıcı byte'ları Worker'a gönderir, Images
  binding görüntüyü WebP'ye çözümler/yeniden kodlar ve yalnızca dönüşüm
  başarılı olursa çıktı stream'i sunucu tarafından üretilen R2 key'ine
  yazılır. WebP çıktısı EXIF/GPS dahil kaynak metadata'sını taşımaz; ham
  kaynak R2'de veya hosted Images'ta saklanmaz. Böylece medya
  byte'ları Davetiye API/VPS üzerinden geçmeden tarayıcıdan Cloudflare'a
  gider. Güvenilir EXIF/GPS temizliğini atlayacağı için Creator fotoğrafında
  tarayıcıdan R2'ye doğrudan PUT yasaktır. Videoda tarayıcıdan Stream'e
  doğrudan upload kullanılabilir.
- API actor/capability, invitation state, module, entitlement, hard
  ceiling, pending usage ve rate limit kontrolünden sonra private
  `PendingUpload` asset ve server-generated key oluşturur.
- API'nin ürettiği upload capability kısa ömürlüdür ve tek bir asset/key/
  Worker route ile sınırlıdır; Worker capability'yi doğrulamadan ve gerçek
  request stream boyutunu saymadan R2'ye yazamaz.
- Upload byte hard ceiling'i provider veya edge upload gateway tarafından
  upload sırasında uygulanır. Worker gerçek stream byte'larını sayar ve
  sınır aşımında stream'i keser; yalnız client `Content-Length` beyanına
  veya upload sonrası HEAD kontrolüne dayanan sınırsız presigned PUT kabul
  edilmez. Aşım partial output/object cleanup ve abuse metriği üretir.
- EXIF/GPS temizliği tarayıcıya bırakılmaz; sağlayıcı tarafında güvenilir
  biçimde yapılır. Decode/dönüşüm başarılı değilse temizlenmiş çıktı
  saklanmaz; entegrasyon testleri R2'deki byte'larda EXIF/GPS kalmadığını
  doğrular.
- Cloudflare master credential browser'a verilmez.
- Client MIME/size/duration/completion authoritative değildir.
- Transformed R2 object server-side metadata/HEAD ve güvenli type
  doğrulamasından;
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
  boyunca kalır. Süresi dolan veya reddedilen upload da provider byte'larını
  davetiye kalıcı purge edilene kadar tutar; expiry/rejection intent ve erişimi
  kapatır, tek başına fiziksel provider deletion başlatmaz. Davetiye medyası
  için fiziksel provider silme yalnız permanent purge/account deletion
  sonrasında yapılır. Bu retention davranışı Fatih tarafından 2026-10-05'te
  PD-16 olarak kabul edilmiştir. Provider deletion doğrulanana kadar
  Rejected/PendingDeletion Creator asset'i mevcut invitation item quota'sında
  sayılır; yalnız Deleted asset slot'u bırakır. Bu, retained bytes'ı mevcut
  davetiye item ve asset byte ceiling'leriyle sınırlar.
- Guest memory upload intent'i ilgili pending memory capability'sine
  bağlıdır.
- SVG/HTML/active content guest allowlist'inde değildir.
- Provider delete idempotent outbox/job ile retry edilir ve reconcile
  edilir.
- Worker→R2 normalize yolundaki Cloudflare Images binding ücretli Images
  aboneliği gerektirir. Cloudflare hesap planının request-body sınırı ile
  Worker CPU/bellek sınırları dış deployment önkoşullarıdır; yapılandırılmış
  hard ceiling'leri karşılamalı, ancak byte limiti uygulama mekanizmasının
  yerine geçmemelidir. Fatih'in 2026-10-04 kararıyla hesap/kaynak kurulumu,
  hesaba özel limit incelemesi ve gerçek provider davranışının kabulü Phase 11
  P11-M6/M8'e ertelenmiştir. Phase 4 local contract tests account behavior'ın
  kanıtı sayılmaz; gerçek medya özelliği bu gate geçene kadar kapalı kalır.

## Sonuçlar

- Presigned URL bearer capability olarak korunmalıdır.
- Fotoğraf için Worker upload capability'si de sınırlandırılmış bir bearer
  capability'dir; yalnızca tek bir pending asset/key ve tek upload'a izin
  verir.
- Görsel ingress yolu, mevcut Cloudflare sağlayıcı sınırının teknik
  açıklamasıdır; yeni bir ürün davranışı değildir. Tarayıcı byte'ları
  doğrudan Cloudflare'a gönderir, sağlayıcıya ait edge kodu kalıcı
  depolamadan önce normalize eder. Video doğrudan Stream'e yüklenebilir.
- Pending intents paralel quota bypass'ını önlemek için kullanıma dahil
  edilir.
- Public page state'i kapandığında yeni asset delivery capability'si
  üretilemediği; image/exchange TTL üst sınırı ve bounded Stream session'ın
  gerçek provider davranışı integration test ile doğrulanmalıdır.
