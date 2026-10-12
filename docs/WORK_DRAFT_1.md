# Çalışma Taslağı 1 — Zarf

**Tarih:** 2026-10-12  
**Durum:** Yerel geliştirme notu. Bu dosya yapılmış işleri ve sonraki şablonda işe yarayacak tasarım kararlarını özetler; doğrulanmamış görsel sonuçları tamamlanmış saymaz.

Bu taslak, Zarf şablonundaki açma/kapama deneyimini ve tema sistemini kayda alır. Ürün kapsamı ve teknik kararlar için `PRODUCT.md`, `ARCHITECTURE.md` ve `AI_WORKFLOW.md` esas alınmalıdır. Bu not tek başına yeni bir ürün özelliğini veya fazı uygulama izni vermez.

## Şablon kimliği ve deneyimi

- Katalogdaki görünen ad **Zarf**. Mevcut davetiye kayıtlarını korumak için teknik anahtar `zarf-davetiye` olarak kalır.
- Önizleme adresi `/sablonlar/zarf-davetiye`.
- Zarf açma/kapama hareketi, iç davetiye kartı ve fotoğraf içerir.
- Özelleştirme alanları: zarf rengi, mühür ve yazı rengi, sayfa atmosferi ve dekor teması.
- İkinci renk seçimi mektup kâğıdına da uygulanır; retro/eskitilmiş kâğıt dokusu bu görünüşün parçasıdır.

## Zarf temasının güncel seçenekleri

Şablon seçicisinde sekiz seçenek görünür:

1. Gül bahçesi
2. Papatya ve kelebek
3. Lale ve uğur böceği
4. Aşk mektubu
5. Yıldızlı uzay
6. Deniz esintisi
7. Müzik notaları
8. Yok

İlk üçü aynı çiçeğin yalnızca rengini değiştiren varyasyonlar değildir; farklı çiçek çizimleri ve eşlik eden küçük canlılar kullanır. Sonraki dört tema kendi görsel diline sahiptir. **Yok**, süs istemeyenler için seçilebilir ve tematik çizimleri kaldırır. Kutup ışıkları gibi eski seçenekler seçicide sunulmaz; eski kayıtların uyumluluğunu koruyan anahtarlar kaynakta bulunabilir.

## Tema sistemi ve sonraki şablona aktarılacak ilkeler

- Seçilebilir sıra `zarfThemePickerKeys`; tema anahtarları, çiçek/canlı eşleşmeleri ve eski kayıt uyumluluğu `zarfTheme.ts` içindedir.
- Her temanın çizimini `ZarfFlowers.tsx` ve ilişkili renderer bileşenleri üretir. Tek bir küçük görseli büyütüp küçültmek farklı tema sayılmaz.
- Temaya uygun özgün şekil, renk, çizgi ve hareket seçilir. Örneğin deniz teması dalga ve kıyı öğeleri; uzay teması gezegen/yıldız öğeleri; çiçek temaları kendi çiçek başı ve canlılarıyla ayrışır.
- Dekorlar ana davetiye resminin arkasında devam edebilir; katman sırası ve kırpma buna göre düşünülür. Dekorların davetiye kenarından başlaması isteniyorsa, kenar konumu ana davetiye geometrisine bağlanır; ekranın sağına/soluna sabitlenmez.
- Renk seçimi hem ilgili ana yüzeyi hem temaya ait yardımcı ayrıntıları tutarlı biçimde etkiler. Seçilen atmosfer tüm sahne arka planında görünür olmalıdır.
- Mobil, tablet ve masaüstü ölçülerinde açılmış ve kapalı hâl; yatay taşma, kesilen çizim, fotoğraf oranı ve kontroller birlikte kontrol edilir.
- Seçici başlığı kısa tutulur; altındaki açıklama hangi parçanın değiştiğini açıkça söyler. Tema seçimi birden fazla dekoru aynı anda sahneye bindirmemelidir.

## Önemli kaynak dosyaları

- Tema anahtarları ve eşleşmeleri: `src/web/src/features/templates/rendering/zarfTheme.ts`
- Tema çizimleri: `src/web/src/features/templates/rendering/ZarfFlowers.tsx`, `ZarfVintage.tsx`
- Zarf görünüşü ve animasyonu: `ZarfEnvelopeRenderer.tsx`, `zarf.css`
- Seçiciler ve açıklamalar: `ZarfLookPickers.tsx`, `templateUi.ts`
- Taslak alanlarının okunması/yazılması: `draftModel.ts`, `InvitationDraftEditor.tsx`
- Backend `garden` izinli değerleri: `src/backend/Davetiye.Application/Modules/Invitations/Contracts/InvitationDraftContracts.cs`

## Çalışma alanı ve devam notu

- Yerel çalışma alanında bu şablonun dışında da değişiklikler bulunabilir. Geri alma veya commit öncesi `git status --short` ve ilgili dosyaların diff'i incelenmelidir.
- Bu dosyada anlatılan görsel hedefler, son ekran görüntüsüyle yeniden karşılaştırılmadan otomatik olarak doğrulanmış kabul edilmemelidir.
- Sonraki şablona başlarken mevcut renderer, seçici ve taslak alanı akışları okunmalı; yalnızca gerekli ortak parçalar yeniden kullanılmalı. Yeni tasarım kendi siluetini, tema dilini ve açılma hareketini taşımalı.
- Test/build yapılmış gibi not düşme; her çalışma oturumunda gerçek komut ve sonuçları kaydet.
