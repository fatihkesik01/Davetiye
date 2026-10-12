# Çalışma Taslağı 2 — Kapı

**Tarih:** 2026-10-12  
**Durum:** Yerel geliştirme sürüyor. Tasarım ve davranış için Fatih'in son ekran görüntüsü geri bildirimleri işlendi; son sütun/kemer/gölge düzenlemeleri gerçek masaüstü ve mobil ekranlarda ayrıca gözden geçirilmeli. Otomatik test, typecheck ve build sonucu bu notta iddia edilmiyor.

Bu dosya, Zarf'tan sonra gelen ikinci şablonun mevcut yapısını ve tasarım kararlarını kaydeder. Yeni bir oturumda başlamadan önce gerçek kaynak kod ve `git status` ile karşılaştır. Ürün ve teknik kararların kaynakları `PRODUCT.md`, `ARCHITECTURE.md` ve `AI_WORKFLOW.md` dosyalarıdır. Bu taslak tek başına yeni özellik veya faz uygulama izni vermez.

## Kimlik ve adresler

- Katalogda görünen ad: **Kapı**.
- İngilizce karşılığı: **Gate**.
- Kararlı teknik anahtar: `dantel-davetiye`. Önceden bu anahtarla kaydedilmiş taslak ve davetiyeler nedeniyle değiştirilmedi.
- Yeni ve tercih edilen adres: `/sablonlar/kapi-davetiye`.
- Eski `/sablonlar/gate` ve `/sablonlar/dantel-davetiye` adresleri geriye dönük uyumluluk için yönlendirilir.
- Katalog sıralamasında Zarf'tan sonra görünür.

## Kapı şablonunda neler var?

- Ortasında birleşen iki kapı kanadı, mühür/monogram ve açılınca kapıların yanlara katlanması.
- Kapı açıldığında alt tarafa doğru devam eden, içeriğiyle beraber uzayan bir davetiye. İçerik için ayrı bir iç scroll alanı kullanılmaz.
- Davetiye başlığı, isimler, fotoğraf, mesaj, tarih, mekân, günün akışı ve desteklenen diğer davetiye bölümleri.
- Özelleştirme alanları: **Kapak kumaşı**, **Dantel ve mühür tonu**, **Sayfa atmosferi** ve **Tema**. Tema alanında yedi görünüş vardır: **Saray Kapısı**, **Japon Bahçesi**, **Akdeniz Avlusu**, **Buz Sarayı**, **Gül Serası**, **Venedik Karnavalı** ve **Yıldız Geçidi**. **Yok**, ek süs istemeyen sade kemerli kapıdır.
- Tarih, davetiyenin bilgi bölümünde zaten bulunduğundan sahnenin altında yinelenmez.

## Temalar nasıl oluşturuldu?

Tema seçimi mevcut davetiye taslak alanı `garden` üzerinden saklanır. Backend izin listesi yedi tema anahtarıyla birlikte `yok` değerini kabul eder. Şema migration'ı gerekmez; API başlangıcındaki katalog eşitlemesi eksik katalog satırını oluşturur, renderer sürümü kod tarafından tanımlanır.

### Saray Kapısı

- Özgün vektör sahnesi `DantelRoyalGate.tsx` içinde SVG ile çizilir. Dış taş çerçeve, kemer, sütun/söve, basamak, fener, ahşap kanatlar, demir işçiliği, aslan tokmakları ve kraliyet arması bu çizimin parçalarıdır.
- CSS renkleri zarf/kumaş ve mühür paleti değişkenlerinden türetilir. Mühür/işleme rengi metal ayrıntılarını ve davetiye içindeki kraliyet süslerini etkiler.
- Kapı açıklığı kemerlidir; davetiye kâğıdının üstü ve iç çerçevesi de aynı kemer siluetini izler.

### Altı yeni tema

- `DantelThemeArtwork.tsx` içinde her tema ayrı SVG sahnesi, kapı kanadı çizimi ve açık davetiyede kenar süsü olarak tanımlıdır. Japon Bahçesi sakura ve fener; Akdeniz Avlusu kemer ve çini; Buz Sarayı kristal kemer ve kar taneleri; Gül Serası cam bölmeler ve sarmaşık; Venedik Karnavalı perde ve maske; Yıldız Geçidi takımyıldızlar ve hilal kullanır.
- Her temanın kendi küçük seçici ikonu, Türkçe/İngilizce adı, kapı kanadı malzeme paleti ve davetiye içi süsü vardır. Kumaş, işleme/mühür tonu ve sayfa atmosferi ortak özelleştirme alanlarından beslenir.
- Gerçekçilik için sekiz seçeneğin her birinde ayrı, sıkıştırılmış WebP malzeme dokusu kullanılır: koyu meşe, Japon lake ahşabı, Akdeniz taşı, buz, sera camı/demiri, Venedik kadifesi, gece metali ve sade meşe. Doku SVG kapı çiziminin üstüne düşük yoğunluklu ışık karışımıyla uygulanır; motif, kontur ve kullanıcının renk seçimleri görünür kalır. Dosyalar `src/web/public/template-textures/gate/` altındadır.
- Kapaklarda ayrıca menteşe plakaları, görünen kenar kalınlığı, yumuşak zemin gölgesi ve temaya özgü ışık yönü kullanılır. Tema motifi kapıdan açık davetiyenin kenarlarına kadar devam eder; içerik alanını kapatmaz.
- Tema anahtarları mevcut `garden` alanıyla saklanır; yeni tema için veritabanı şeması değişikliği gerekmez.

### Yok

- Tema süsleri ve üst saçak gösterilmez; seçenek dekor istemeyenler için sade kalır.
- İki sade kanat, dış kenarlardaki sütunların içinden başlar ve kapalıyken üstte kemerli bir açıklık oluşturur. Sütunlar kapaklardan bağımsız sahne katmanında kalır.
- Sade görünüşü süssüz tutarken yüzeyi boş bırakmamak için kanatlara gömme panel, ince ahşap damarı, sade kulp ve ışık/gölge kenarları eklenmiştir.
- Kâğıt yine kemerli görünür ve açılışta kanat/sütun açıklığından daha geniş görünmeyecek şekilde ölçülür.

### Yeni tema eklerken dikkat edilecekler

- Aynı resmi büyütüp küçültmek veya yalnızca rengini değiştirmek yeni tema oluşturmaz. Her tema kendi silueti, malzemesi, süsleri ve görsel hiyerarşisiyle tanınmalıdır.
- Tema adı ile küçük seçici ikonu, temanın sahnede gerçekten çizdiği şeyi anlatmalıdır.
- Yeni çizimler dekor alanına özgü bileşende tutulmalı; davetiye içeriğinin veri yapısı veya diğer temaların davranışı gereksiz yere değiştirilmemelidir. Tema seçenekleri `ZarfLookPickers.tsx`, görünen adları `templateUi.ts`, saklanan anahtarları `zarfTheme.ts` ve backend izin listesini birlikte güncelle.
- Renkler ilgili tema paletine bağlanmalı; seçilen renk kontur, işleme, mühür ve kâğıt gibi uygun yüzeylerde anlamlı etki göstermelidir. Atmosfer rengi sayfa sahnesinde kalmalı, kumaş renginin yerine geçmemelidir.
- Açılış animasyonunda kapılar kaybolmamalı, içerik ya da sahne boyu bir anda sıçramamalı; kapaklar kapı menteşesi gibi katlanmalı. Açılış ve kapanış süreleri senkronize olmalıdır.
- Kapalı/açık hâller masaüstü ve dar telefon ekranında kontrol edilmelidir. Yatay scroll, üst/yan beyaz şerit, sütun dışına taşan davetiye, kırpılmış süs, zıplayan sayfa ve kapak yüksekliği uyumsuzluğu aranmalıdır.
- Uzun örnek içerik (özellikle çok satırlı gün akışı) sayfanın doğal olarak uzadığını göstermelidir. İçeriği sabit yüksekliğe sıkıştırma veya içeride scroll açma.
- `prefers-reduced-motion`, klavye odağı ve ekran okuyucu adları korunmalıdır.

## Örnek içerik

Kapı demosunda görünüşü uzun sayfada incelemek için sekiz program adımı bulunur: karşılama/kokteyl, nikâh, aile fotoğrafları, akşam yemeği, ilk dans, pasta, müzik ve veda. Bunlar yalnızca demo verisidir; gerçek davetiye taslağına veya kullanıcı verisine yazılmaz. Kaynak: `src/web/src/features/templates/rendering/demoData.ts`.

## Önemli uygulama dosyaları

- Ana renderer ve kemersiz/sade sütun sahnesi: `src/web/src/features/templates/rendering/DantelInvitationRenderer.tsx`
- Kraliyet kapısı SVG çizimleri: `src/web/src/features/templates/rendering/DantelRoyalGate.tsx`
- Kapak, açılma animasyonu, kemerli kâğıt ve tema stilleri: `src/web/src/features/templates/rendering/dantel.css`
- Tema, renk seçicileri ve metinleri: `ZarfLookPickers.tsx`, `zarfTheme.ts`, `zarfAccent.ts`, `templateUi.ts`
- Demo verisi, katalog/URL ve renderer kaydı: `demoData.ts`, `catalog/templateRouting.ts`, `catalog/localTemplateCatalog.ts`, `registry.tsx`, `registry.manifest.json`
- Backend katalog tanımı ve izinli tema anahtarı: `CodeOwnedTemplateCatalog.cs`, `InvitationDraftContracts.cs`

## Sonraki inceleme adımları

1. `/sablonlar/kapi-davetiye` adresinde **Saray Kapısı** ve **Yok** temalarını ayrı ayrı aç/kapat.
2. Kapalı sahnede sütun/kanat hizasını; açık sahnede kemerli davetiye üstünü, dış sınırları ve kalan gölgeleri kontrol et.
3. Uzun gün akışında sayfanın aşağı doğru doğal büyüdüğünü, iç scroll veya sayfa sıçraması olmadığını kontrol et.
4. Masaüstü ve dar telefon genişliğinde yatay taşma, üst boşluk, sütun çakışması ve kenarlardan taşma olmadığını kontrol et.
5. Görsel inceleme yapılmadıysa bunu açıkça yaz. Test/build ancak gerçekten çalıştırıldıysa sonuç olarak kaydet.

## Güvenli çalışma sınırı

Yerel değişiklikler commit edilmemiş olabilir. `git status --short` ve ilgili diff'leri incelemeden toplu geri alma yapma. VPS bağlantısı/deploy gerekirse önce `docs/DEPLOYMENT.md` oku; Lora'ya dokunma. Bu dosya VPS erişimi veya yayınlama izni değildir.
