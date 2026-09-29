# ADR-0002 — Identity, Authorization, publicCode ve Guest Capability'leri

Durum: **Accepted**  
Tarih: **2026-09-28**

## Bağlam

Creator ve Admin authenticated, Guest hesapsızdır. Public invitation
linki görüntüleme sağlar fakat yönetim yetkisi veremez. Anonymous RSVP ve
gift işlemleri aynı browser'da sınırlı yönetim gerektirir.

## Karar

- ASP.NET Core Identity ve server-managed cookie kullanılacak.
- Creator yetkisi query seviyesinde internal resource ID + current
  `OwnerAccountId` ile doğrulanacak; account ID client'tan alınmayacak.
- Super Admin ayrı MFA-complete policy kullanacak; aggregate-only yönetim
  yüzeyi olacak ve özel guest içeriğine genel bypass verilmeyecek.
- `publicCode` en az 128-bit CSPRNG, immutable, unique ve locator-only
  olacak. Slug decorative'dır.
- Başlangıç zamanı gelmemiş Scheduled, Paused, Expired ve banned için PII-free
  unavailable; deleted veya unknown için 404 uygulanacak. Stored Scheduled
  kaydın uygun window'u başlamışsa public gate effective Active sonucu verir;
  worker gecikmesi erişimi geciktirmez.
- RSVP capability submission-scoped, Gift capability invitation-scoped
  `GuestGiftSession` olacak.
- Memories için kalıcı guest management token olmayacak; yalnız kısa
  ömürlü upload/finalize capability kullanılacak.
- Guest token en az 256-bit olacak; raw token HttpOnly/Secure cookie'de,
  purpose/resource-scoped HMAC digest DB'de tutulacak.
- Anonymous unsafe çağrılar antiforgery/origin kontrolü ve rate limit
  uygulayacak.
- Anonymous create/increase ve kullanımı artırabilen update'ler aynı
  effective-Active, module-enabled, entitlement ve quota policy'sini geçer.
  Gift cancel gibi azaltıcı işlem kendi capability'siyle yapılabilir ve kota
  aşımıyla engellenmez; bu ban/delete/access gate'ini kaldırmaz ve capability
  başka resource/Creator yetkisine genişlemez.
- Production Admin bootstrap public HTTP endpoint değil, kontrollü
  one-time operation olacak; MFA TOTP + recovery code kullanacak.

## Sonuçlar

- Public link kaybı Creator hesabını tehlikeye atmaz.
- Cookie kaybı RSVP duplicate oluşturabilir ve Gift cancel erişimini
  kaybettirebilir; bu kabul edilmiş MVP kısıtıdır.
- Guest capability analytics/visitor tracking amacıyla kullanılmaz.
- Cross-account ve cross-capability negative integration testleri release
  gate'idir.
