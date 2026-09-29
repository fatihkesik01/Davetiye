# ADR-0003 — Invitation Lifecycle, İçerik Snapshot'ları ve Trash

Durum: **Accepted with open product decisions**  
Tarih: **2026-09-28**

## Bağlam

Draft autosave edilmelidir; Active içerikte her tuş vuruşu public'e
gitmemelidir. Scheduled/Paused/Expired içerik özel bilgi sızdırmamalı ve
silme configurable retention sonrasında purge edilmelidir.

## Karar

- Stored states: Draft, Scheduled, Active, Paused, Expired.
- Soft delete `deletedAt/purgeAfter` overlay'idir; enum state değildir.
- Ban account overlay'idir; invitation state'i değiştirmez.
- Bir current WorkingContent ve bir current PublishedContent tutulur;
  version history yoktur.
- Publish/Update PublishedContent'i atomik olarak yeniler.
- PublicationWindow UTC `startsAt/endsAt` tutar; pause `endsAt` değerini
  değiştirmez.
- Public gate state, clock, ban, deletion ve grant'i request anında
  değerlendirir; background job security boundary değildir.
- Stored Scheduled state, uygun PublicationWindow başladığında effective
  Active; window bittiyse effective Expired değerlendirilir. Worker gecikmesi
  public yayını başlatamaz veya geciktiremez.
- Bu effective state Creator command/read model için de authoritative'dir;
  stored Scheduled fakat effective Active kayıt Active action set'i ve
  template lock uygular. İçerik update semantiği açık ürün kararıdır.
- Active template değişmez; Paused durumda değiştirilebilir.
- Preview salt-okunur/simülasyondur ve guest mutation/stat üretmez.
- Restore eski state'i körlemesine public yapmaz.
- Purge idempotent ve provider media silmeleri retry-safe olur.

## Kabul Edilmemiş Açık Kararlar

- Scheduled cancel/reschedule/hemen yayınla
- Trash restore hedef state'i
- Expired invitation'ın geleceğe Scheduled edilmesi
- Scheduled durumdayken autosave'in başlangıçta otomatik yayınlanması veya
  explicit Update gerektirmesi

Bu davranışlar PRODUCT kararı olmadan implement edilmez.

## Sonuçlar

- Autosave ile live content birbirinden ayrılırken kapsamlı revision
  history eklenmez.
- Job gecikmesi Paused/Expired içeriği görünür yapamaz.
- Trash retention sırasında content geri yüklenebilir; purge sonrası
  invitation-owned veri ve provider media kalıcı temizlenir.
