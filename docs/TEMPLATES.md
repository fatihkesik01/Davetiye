# Şablonlar (Taslaklar)

Bu doküman şablon/taslak tasarımlarının kaydıdır: her şablonun konsepti,
kullanıcının seçebildiği parametreler ve durumu. Faz planlarından bağımsızdır;
bir şablonun burada yer alması bir fazın kapsamına girdiği anlamına gelmez.

Ürün kuralları `docs/PRODUCT.md` §5, teknik sözleşme
`docs/adr/0007-template-renderer-version-contract.md` içindedir. Şablonlar
kod sahiplidir (React renderer); kullanıcı HTML/CSS yazmaz ve serbest
tasarım/sürükle-bırak yapmaz: yalnızca şablonun sunduğu parametreleri seçer.

Ayrıntılı çalışma notları: `docs/WORK_DRAFT_1.md` (Zarf), `docs/WORK_DRAFT_2.md` (Kapı).

Katalog sırası: **1. Zarf**, **2. Kapı**, ardından sekiz başlangıç şablonu
(`TemplateCatalogService` içindeki "öne çıkanlar önce" adımı).

## 1. Zarf (`zarf-davetiye`) — ana 1. taslak

Katalogdaki ad "Zarf Davetiye" / "Zarf", kategori "Düğün / Tarih Duyurusu".
Ücretsiz, `rendererVersion` 1. Önizleme: `/sablonlar/zarf-davetiye`.

**Konsept:** Davetiye bir zarf olarak açılır. Misafir kapak ekranında mühürlü
zarfa dokunur, mühür kırılır, kapak açılır ve içinden kalp şekilli kart ile bir
fotoğraf çıkar. İçerik zarf açılana kadar sayfaya hiç eklenmez. İlham: blahaus.co'nun
zarf açılışlı reel'i; tasarım sabittir, kullanıcı yalnızca parametreleri seçer,
görsel varlıklar kopyalanmaz (CSS/SVG ile özgün çizilir).

**Kullanıcının seçtikleri** (hepsi isteğe bağlı; bilinmeyen değer varsayılana düşer,
serbest hex kabul edilmez):

| Parametre | Açıklama |
|---|---|
| Zarf rengi (`accentColor`) | Zarf gövdesi, kapak ve sayfanın açık tonu. 8 anahtar: `adacayi` (varsayılan), `pudra-pembe`, `krem`, `bordo`, `lacivert`, `lavanta`, `seftali`, `zumrut`. |
| Mühür ve yazı rengi (`accentColor2`) | İkinci renk: mühür, kartın isim/tarih yazısı, altın çizgiler, kapak kenarı ve mektubun iç kâğıdı. 8 anahtar: `altin` (varsayılan), `gumus`, `bakir`, `bordo`, `zumrut`, `lacivert`, `gulkurusu`, `fildisi`. Yazı rengi kartta AA okunurluk için ayrı bir koyu tona türetilir. |
| Ortam (`ambiance`) | Sayfanın tüm atmosferi (arka plan ve metin tonları), yalnızca renk/atmosfer. 8 anahtar: `sabah` (varsayılan), `cayir`, `gunbatimi`, `gece`, `safak`, `orman`, `okyanus`, `lila`. `gece` ve `orman` koyu zemindir; açık metin tonlarına geçer. Arka plan tüm sayfa genişliğini boyar. |
| Bahçe/tema (`garden`) | Yan dallarda büyüyen çerçeve, zarf cebi ve kalbin üst lobları için tema. Seçicide 8 seçenek: Gül bahçesi (`gul`, varsayılan), Papatya ve kelebek (`papatya-kelebek`), Lale ve uğur böceği (`lale-ugurbocegi`), Aşk mektubu (`ask-mektubu`), Yıldızlı uzay (`yildizli-gece`), Deniz esintisi (`deniz-esintisi`), Müzik notaları (`muzik-notalari`) ve Yok (`yok`, süssüz). Backend 21 anahtarı kabul eder (Kapı temaları ve eski seçenekler dahil); seçicide sunulmayan eski anahtarlar kayıtlı davetiyelerin görünümünü korur. Eski `flora` alanı geriye dönük okunur, `garden` olarak kaydedilir. |
| Fotoğraf | **Bir** fotoğraf, mevcut Cover medya rolüyle. Kart yanındaki çerçeveli baskıda görünür; yoksa zarif yer tutucu. |
| Metinler | İsimler (`hostNames`), başlık, tarih, mekan, kısa mesaj. Kartta yalnızca isimler ve tarih görünür; mekan, saat ve program altta akar. Mesaj girilmezse sıcak bir varsayılan cümle (TR/EN) gösterilir. |

**Davranış ve kısıtlar**

- Zarfa/düğmeye dokununca mühür kırılır ve zarf açılır; "Zarfı kapat" ters animasyonla
  kapatır (demoda kapat düğmesi alt çubukta, public/önizlemede kartın altında).
  Durum asla saklanmaz: her yüklemede kapalı başlar.
- Açılınca iki kenardan tema dalları büyür (temaya göre çiçek/kelebek/arı/uğur
  böceği/yaprak vb.), kapanınca davetiyeyle birlikte hızla kaybolur.
- Kapak ekranında başlık yok (isimler kartta); görünmez `h1` ekran okuyucu içindir.
- Animasyonlar yalnızca `transform`/`opacity` ile yapılır, filtre ve büyük SVG
  `<pattern>` kullanılmaz (performans/donma deneyimi); `prefers-reduced-motion`
  için anında açılır; klavye ile erişilebilir. Yatay kaydırma yok, zarf açma/kapama
  ve seçenek değişiminde yerleşim kayması yok.
- Responsive: tek URL, telefon/tablet/masaüstü. Preview ve public aynı renderer'ı kullanır.
- Demo: `/sablonlar/zarf-davetiye` tam sayfa (üyelik gerekmez); alttaki çubukta
  "Özelleştir" paneli renk, ikinci renk, ortam ve temayı seçtirir. Demo örnek:
  Hande & Fatih, 10 Ekim 2026.
- Creator editöründe aynı seçiciler yalnızca bu şablon seçiliyken görünür ve mevcut
  taslak otomatik kaydıyla (`accentColor`, `accentColor2`, `ambiance`, `garden`) kaydedilir.
- Gerçek fotoğraf yükleme Cloudflare medya hattına bağlıdır (`docs/DEPLOYMENT.md`:
  Phase 11 kapılarına kadar kapalı). Demo ve creator/önizleme, creator'ın Cover'ı yokken
  paketlenmiş demo fotoğrafı gösterir; yayınlanmış public sayfada gösterilmez.
- Çeyiz (hediye listesi) modülü bu şablonda desteklenir (`giftRegistry`).
  Misafirin görmesi için planda `giftRegistryEnabled` de gerekir (ücretsiz planda kapalı).

**Demo fotoğrafı:** "two person holding hands", Belle Collective,
[Unsplash](https://unsplash.com/photos/two-person-holding-hands-QdFkiLk5onU)
(Unsplash Lisansı: ticari kullanım serbest, atıf zorunlu değil; kaynak yine de burada
kayıtlıdır). Dosya: `src/web/public/template-demo/zarf-davetiye-photo.jpg` (900x1349).

**Kaynak dosyalar:** `src/web/src/features/templates/rendering/` altında
`ZarfEnvelopeRenderer.tsx`, `zarf.css`, `ZarfFlowers.tsx`, `ZarfVintage.tsx`,
`zarfTheme.ts`, `zarfAccent.ts`, `ZarfLookPickers.tsx`; metinler `src/i18n/templateUi.ts`;
backend izinli anahtarlar `InvitationDraftContract` (`InvitationDraftContracts.cs`).

## 2. Kapı (`dantel-davetiye`)

Katalogdaki ad **Kapı** (EN: Gate), kategori "Düğün / Nişan". Teknik anahtar eski
kayıtlar nedeniyle `dantel-davetiye` kalır; tercih edilen adres
`/sablonlar/kapi-davetiye` (`/sablonlar/gate` ve `/sablonlar/dantel-davetiye` yönlendirilir).
Ücretsiz, `rendererVersion` 1.

**Konsept:** Ortasında birleşen iki kanatlı kapı. "Kapıları aç" ile kamera kapıya
yaklaşır (sahne kapı açıklığının merkezine doğru büyür, taş çerçeve silinmez, kenarlardan
taşar), kanatlar iki yana katlanır ve davetiye kapının eşiğinden aşağı doğru uzar.
Kapatınca ters yönde sarılır. Davetiye kâğıdı tüm kapı temalarında **aynı genişlik, yükseklik
ve kenar boşluğuna** sahiptir; yalnızca süsler değişir.

**Kullanıcının seçtikleri:**

| Parametre | Açıklama |
|---|---|
| Kapak kumaşı (`accentColor`), Dantel ve mühür tonu (`accentColor2`), Sayfa atmosferi (`ambiance`) | Zarf ile aynı alanlar ve anahtarlar; kapı kanatlarının malzeme/kumaş tonunu, mühür/işleme rengini ve tüm sayfa arka planını etkiler. |
| Tema (`garden`) | 7 görünüş + Yok: Saray Kapısı (`saray-kapisi`), Japon Bahçesi (`japon-bahcesi`), Akdeniz Avlusu (`akdeniz-avlusu`), Buz Sarayı (`buz-sarayi`), Gül Serası (`gul-serasi`), Venedik Karnavalı (`venedik-karnavali`), Yıldız Geçidi (`yildiz-gecidi`), Yok (`yok`, sade kemerli kapı). |
| Fotoğraf, metinler | Zarf ile aynı (Cover rolü; isimler, tarih, mekan, mesaj, program, isteğe bağlı bölümler). |

**Davranış:** İçerik kapı açılana kadar görünmez; kapı durumu saklanmaz; açılışta
yalnızca `transform`/`clip-path`/`opacity` kullanılır; `prefers-reduced-motion` anında geçer;
tarih davetiye bilgi bölümünde bulunduğundan sahnenin altında yinelenmez. Saray Kapısı
özgün SVG sahnesidir (taş kemer, sütun, fener, aslan tokmaklı demir işlemeli kanatlar,
armalı kilit taşı, basamak); diğer temalar `DantelThemeArtwork.tsx` içinde kendi sahnesine,
malzeme dokusuna (`public/template-textures/gate/*.webp`) ve davetiye içi süsüne sahiptir.
Çeyiz modülü desteklenir (`giftRegistry`).

**Demo:** `/sablonlar/kapi-davetiye`, uzun sayfada incelemek için sekiz program adımı
içerir (yalnızca demo verisi). Alt çubukta tek aç/kapat düğmesi ve "Özelleştir" paneli bulunur.

**Bilinen zayıf noktalar** (son inceleme): Saray Kapısı düz renk gölgelendirme kullanır,
doku yok ve taç biraz geometriktir; dantel temasında 1280px'te katlanan kanatlar kumaştan
çok açılı kanat gibi görünür; uzun telefon ekranlarında kapalı sahnenin altında boşluk
kalır; kamera yakınlaşması bitince Saray Kapısı yan sütunlarla kalabalıklaşır. CPU 4x
yavaşlatmada açılışta hâlâ ~90ms'lik tek bir uzun görev vardır (normal hızda yok).

**Kaynak dosyalar:** `DantelInvitationRenderer.tsx`, `DantelRoyalGate.tsx`,
`DantelThemeArtwork.tsx`, `dantelThemes.ts`, `dantel.css`, `catalog/templateRouting.ts`,
`catalog/localTemplateCatalog.ts` (yalnızca geliştirme modunda), `registry.tsx`;
backend: `CodeOwnedTemplateCatalog.cs`.

## 3. Ortak notlar

- **Günün akışı:** creator editörü (4. adım, tüm şablonlar) çok kayıtlı program listesini
  destekler: satır ekle/sil, ↑/↓ ile sırala, "Saate göre sırala". Backend sınırları:
  en çok 20 kayıt, başlık 200, açıklama 1000 karakter; hiçbir alan zorunlu değildir;
  açıklamada satır sonu yasaktır.
- **Çeyiz modülü:** `giftRegistry` modülü zarf ve kapı şablonlarında desteklenir. Mevcut
  veritabanı satırına yansıması için veri migration'ı `P12ZarfGiftRegistryModule`
  vardır (yalnızca önceki seed değerine eşit satırı günceller). Dantel/Kapı satırı ilk
  başlatmada eklenir.
- **Başlatma kodu yalnızca eksik şablon satırlarını ekler**; mevcut satırların metadata'sını
  (ad, kategori, modüller) güncellemez (ADR-0007). Bu yüzden VPS'teki zarf satırı eski
  kategori adını taşıyabilir; arayüz kategoriyi `templateLabels.ts` ile düzeltir.
- Yeni tema/şablon eklerken: aynı çizimi büyütüp küçültmek veya yalnızca renk
  değiştirmek tema sayılmaz; backend izin listesi, `zarfTheme.ts`, seçiciler ve
  `templateUi.ts` birlikte güncellenir; atmosfer rengi kumaş renginin yerine geçmez;
  açık/kapalı hâller 360-1280px'te yatay taşma, kesilen çizim ve kayma açısından kontrol edilir.

## 4. Mevcut başlangıç şablonları

`zamansiz-dugun`, `romantik-nisan`, `gece-kina`, `neseli-sunnet`,
`renkli-dogum-gunu`, `pastel-baby-shower`, `modern-mezuniyet`,
`minimal-acilis`. Bunlar `docs/PRODUCT.md` §5'teki başlangıç setidir ve final
tasarım değildir; ileride ayrı ayrı yeniden tasarlanabilir.
