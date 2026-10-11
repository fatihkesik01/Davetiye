# Şablonlar (Taslaklar)

Bu doküman şablon/taslak tasarımlarının kaydıdır: her şablonun konsepti,
kullanıcının seçebildiği parametreler ve durumu. Faz planlarından bağımsızdır;
bir şablonun burada yer alması bir fazın kapsamına girdiği anlamına gelmez.

Ürün kuralları `docs/PRODUCT.md` §5, teknik sözleşme
`docs/adr/0007-template-renderer-version-contract.md` içindedir. Şablonlar
kod sahiplidir (React renderer); kullanıcı HTML/CSS yazmaz ve serbest
tasarım/sürükle-bırak yapmaz: yalnızca şablonun sunduğu parametreleri seçer.

## 1. Zarf Davetiye (`zarf-davetiye`) — ana 1. taslak

Katalogda **ilk** sırada yer alır. Durum: geliştirme aşamasında.

**Konsept:** Davetiye bir zarf olarak açılır. Misafir kapak ekranında
"Open the Invitation" çağrısına dokunur, zarfın kapağı açılır ve içinden
"Save the Date" kartı ile bir fotoğraf çıkar. Ardından davetiyenin diğer
modülleri (tarih/mekan, geri sayım, RSVP vb.) normal akışla gösterilir.

**İlham:** blahaus.co'nun zarf açılışlı dijital davetiye reel'i. Orada
içerik tasarımcı tarafından serbestçe yerleştiriliyordu; bizde tasarım
sabittir, kullanıcı yalnızca aşağıdaki parametreleri seçer. Görsel varlıklar
kopyalanmaz, CSS/SVG ile özgün çizilir.

**Kullanıcının seçtikleri**

| Parametre | Açıklama |
|---|---|
| Renk (`accentColor`) | **Tek** renk. Hazır paletten bir anahtar: `adacayi` (varsayılan), `pudra-pembe`, `krem`, `bordo`, `lacivert`, `lavanta`. Zarf, kapak/mühür, vurgular ve arka planın açık tonu bu tek renkten türetilir. Serbest hex kabul edilmez. |
| Ortam (`ambiance`) | Sayfanın tüm atmosferi, **tek** seçim: `sabah` (sabah bahçesi, ışık huzmeleri, **kelebekler**; varsayılan), `cayir` (bal rengi çayır, **arılar**), `gunbatimi` (şeftali/mercan/altın gökyüzü, **yusufçuklar**), `gece` (yıldızlı lacivert gece, **ateş böcekleri**; metin ve düğme renkleri koyu zemine göre açık tonlara geçer), `safak` (pembe şafak, dallarda **uğur böcekleri**). Arka plan degradesi ve metin renkleri ortama göre değişir; küçük canlılar yalnızca zarf açıkken yan dallar boyunca süzülür. Bilinmeyen değer varsayılana düşer. |
| Çiçek (`flora`) | Yan dallardaki büyüyen çerçevenin, zarf cebinin ve kartın üst loblarındaki çiçek türü, **tek** seçim: `gul` (varsayılan), `papatya`, `lale`, `lavanta`, `kiraz` (kiraz çiçeği; birkaç yaprağı yavaşça düşer). Her türün kendi doğal renkleri zarf rengiyle hafifçe tonlanır. Bilinmeyen değer varsayılana düşer. |
| Fotoğraf | **Bir** fotoğraf, mevcut Cover medya rolüyle. Zarftan çıkan kartın yanındaki çerçeveli baskıda görünür. Fotoğraf yoksa zarif bir yer tutucu gösterilir. |
| Metinler | İsimler (`hostNames`), başlık, tarih, mekan, kısa mesaj. Kartta yalnızca isimler ve tarih görünür; mekan, saat ve program altta akar. Mesaj girilmezse sıcak bir varsayılan cümle gösterilir (TR/EN). |

**Davranış ve kısıtlar**

- Açılış animasyonu CSS/React ile yapılır (video yok); `prefers-reduced-motion`
  için anında açılır; klavye ile erişilebilir.
- Responsive: tek URL, telefon/tablet/masaüstü.
- Preview ve public aynı renderer'ı kullanır.
- Demo verisiyle `/sablonlar` sayfasında üyelik olmadan önizlenir.
- Gerçek fotoğraf yükleme Cloudflare medya hattına bağlıdır
  (`docs/DEPLOYMENT.md`: medya Phase 11 kapılarına kadar kapalı); demo ve
  önizleme paketlenmiş örnek görselle çalışır.

**Demo fotoğrafı:** "two person holding hands", Belle Collective,
[Unsplash](https://unsplash.com/photos/two-person-holding-hands-QdFkiLk5onU)
(Unsplash Lisansı: ticari kullanım serbest, atıf zorunlu değil; yine de
kaynak burada kayıtlıdır). Dosya: `src/web/public/template-demo/zarf-davetiye-photo.jpg`
(900x1349). Yalnızca creator'ın Cover fotoğrafı yokken demo, editör önizlemesi
ve `/onizleme`'de görünür; yayınlanmış public sayfada gösterilmez.

**Demo ve editör:** `/sablonlar/zarf-davetiye` tam sayfa demodur; alttaki çubukta "Özelleştir" paneli üç seçiciyi (Zarf rengi, Ortam, Çiçek) açar, "Zarfı kapat" ve "Bu şablonla başla" çubukta kalır. Creator editöründe aynı üç seçici şablon adımında yalnızca bu şablon seçiliyken görünür ve mevcut taslak otomatik kaydıyla (`accentColor`, `ambiance`, `flora`) kaydedilir. Varsayılan: sabah + gül + adaçayı.

**Teknik not:** Yeni code-owned şablon; mevcut sekiz şablonla aynı
`(templateKey, rendererVersion)` sözleşmesini kullanır. `accentColor`,
taslak içeriğe eklenen isteğe bağlı bir alandır.

## 2. Mevcut başlangıç şablonları

`zamansiz-dugun`, `romantik-nisan`, `gece-kina`, `neseli-sunnet`,
`renkli-dogum-gunu`, `pastel-baby-shower`, `modern-mezuniyet`,
`minimal-acilis`. Bunlar `docs/PRODUCT.md` §5'teki başlangıç setidir ve final
tasarım değildir; ileride ayrı ayrı yeniden tasarlanabilir.
