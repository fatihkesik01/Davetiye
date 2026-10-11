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
| Fotoğraf | **Bir** fotoğraf, mevcut Cover medya rolüyle. Zarftan çıkan kartın yanındaki çerçeveli baskıda görünür. Fotoğraf yoksa zarif bir yer tutucu gösterilir. |
| Metinler | İsimler (`hostNames`), başlık, tarih, mekan, kısa mesaj. |

**Davranış ve kısıtlar**

- Açılış animasyonu CSS/React ile yapılır (video yok); `prefers-reduced-motion`
  için anında açılır; klavye ile erişilebilir.
- Responsive: tek URL, telefon/tablet/masaüstü.
- Preview ve public aynı renderer'ı kullanır.
- Demo verisiyle `/sablonlar` sayfasında üyelik olmadan önizlenir.
- Gerçek fotoğraf yükleme Cloudflare medya hattına bağlıdır
  (`docs/DEPLOYMENT.md`: medya Phase 11 kapılarına kadar kapalı); demo ve
  önizleme paketlenmiş örnek görselle çalışır.

**Teknik not:** Yeni code-owned şablon; mevcut sekiz şablonla aynı
`(templateKey, rendererVersion)` sözleşmesini kullanır. `accentColor`,
taslak içeriğe eklenen isteğe bağlı bir alandır.

## 2. Mevcut başlangıç şablonları

`zamansiz-dugun`, `romantik-nisan`, `gece-kina`, `neseli-sunnet`,
`renkli-dogum-gunu`, `pastel-baby-shower`, `modern-mezuniyet`,
`minimal-acilis`. Bunlar `docs/PRODUCT.md` §5'teki başlangıç setidir ve final
tasarım değildir; ileride ayrı ayrı yeniden tasarlanabilir.
