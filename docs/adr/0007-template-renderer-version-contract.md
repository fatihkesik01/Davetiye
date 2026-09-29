# ADR-0007 — Template Renderer Version Sözleşmesi

Durum: **Accepted**  
Tarih: **2026-09-28**

## Bağlam

İlk yaklaşık sekiz template daha sonra yeniden tasarlanabilir. Bir
template deploy'u yayınlanmış davetiyeleri bozmamalı ve DB kullanıcı
tarafından çalıştırılabilir template kodu içermemelidir.

## Karar

- Renderer'lar code-owned React component'larıdır.
- DB `TemplateDefinition` içinde stable key, metadata, active,
  free/premium, preview, supported modules ve required/recommended field
  metadata tutar.
- Renderer registry `(templateKey, rendererVersion)` ile resolve eder.
- Working ve Published snapshot kendi template key/renderer version'ına
  pinlenir.
- Eski renderer kullanan published invitation kalırken sürüm kaldırılmaz;
  migration ayrı kontrollü iştir.
- Preview ve public aynı normalized render model/renderer'ı kullanır.
- Kullanıcı/Super Admin executable HTML/CSS/JavaScript yazamaz.
- Template tasarımı domain/API'yi template'e özgü branch'lerle
  çoğaltmaz.

## Sonuçlar

- Template yeniden tasarımı mimari rewrite gerektirmez.
- Metadata deploy olmadan yönetilebilir; yeni renderer davranışı code
  deploy'u gerektirir.
- Template contract ve visual regression testleri release gate'idir.
