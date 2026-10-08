# Dijital Davetiye Platformu --- Ürün Kapsam Özeti

## 1. Ürünün Ana Fikri

Platform, kullanıcıların teknik veya tasarım bilgisine ihtiyaç duymadan
birkaç dakika içinde profesyonel bir **dijital davetiye web sayfası**
oluşturmasını sağlayacak.

Düğün ana kullanım alanlarından biri olsa da sistem yalnızca düğüne
bağlı olmayacak. Nikâh, nişan, kına, söz, doğum günü, baby shower,
sünnet, mezuniyet ve özel etkinlik gibi farklı türler desteklenebilecek.

Kullanıcı Canva gibi öğeleri sürükleyip bırakmayacak. **Şablon seçip
bilgilerini dolduracak.** Sistem bu bilgileri profesyonel tasarımın
içine otomatik yerleştirecek.

## 2. Kullanıcı Türleri

### Creator / Davetiye Sahibi

Davetiyeyi oluşturan bireysel kullanıcı veya organizasyon hesabıdır.
Kendi davetiyelerini oluşturur, düzenler, yayınlar, pasife alır, siler
ve gelen RSVP/anı/hediye bilgilerini yönetir.

MVP'de organizasyon hesabı basit bir hesap türüdür ve tek kullanıcı
tarafından yönetilir. Organization membership, ekip daveti, çalışan
rolleri ve workspace sistemi MVP kapsamında değildir.

### Davetli

Hesap açmaz. Kendisine gönderilen public davetiye linkine girer.
Creator'ın açtığı özelliklere göre RSVP doldurabilir, anı bırakabilir,
fotoğraf/video yükleyebilir veya hediye seçebilir.

### Super Admin

Platform yönetim tarafıdır. Normal kullanımda kullanıcıların özel RSVP
cevapları veya davetiye içerikleriyle ilgilenmez. Genel kullanıcı,
davetiye, paket, ödeme, depolama ve sistem durumunu yönetir.
Gerektiğinde kullanıcı banlayabilir.

## 3. Hesap Sistemi

-   Creator hesap açar.
-   E-posta + şifre ve Google ile giriş MVP'de desteklenir.
-   E-posta doğrulama, şifre sıfırlama ve güvenli oturum yönetimi
    bulunur.
-   Google web OAuth localhost dışında raw IP callback kabul etmediği
    için ilk IP deployment email/password ile kullanılabilir; Google
    login localhost geliştirmesinde test edilir ve production domain +
    HTTPS yapılandırıldığında etkinleştirilir.
-   IP üzerinden plain HTTP yalnız private smoke/health kullanımıdır.
    Gerçek kullanıcı email/password ve session trafiği güvenilir HTTPS
    olmadan açılmaz.
-   Telefon/SMS altyapısı ileride kullanılabilecek şekilde düşünülebilir
    ancak MVP bağımlılığı değildir.
-   Bireysel ve organizasyon hesabı ayrımı bulunur.
-   Organizasyon hesapları çok sayıda müşteri davetiyesi yönetebilir.
-   Bir organizasyon hesabını MVP'de yalnızca hesabın sahibi olan tek
    kullanıcı yönetir.

## 4. Davetiye Oluşturma

Ana hedef: **Kullanıcı mümkün olan en kısa sürede davetiyesini
oluşturabilmeli.**

Örnek akış:

`Etkinlik türü → Temel bilgiler → Şablon → Tarih/Mekan → İsteğe bağlı bölümler → Önizleme → Yayınla`

Her adımı doldurmak zorunlu değildir. Galeri, RSVP, program, anılar ve
hediye listesi gibi özellikler **Şimdilik Geç** denilerek atlanabilir ve
daha sonra eklenebilir.

Yeni davetiye oluşturulurken bilgiler otomatik kaydedilir. Browser
storage yalnızca bağlantı kopması gibi durumlarda kurtarma amacıyla
kullanılabilir; esas veri backend'de tutulur.

## 5. Şablon Sistemi

-   İlk etapta AI/coding agent tarafından yaklaşık **8 kaliteli başlangıç
    şablonu** oluşturulur.
-   Bu sekiz şablon final tasarım seti değildir. İleride UI/UX agent ile
    ayrı ayrı yeniden tasarlanabilir ve geliştirilebilir.
-   Şablonlar ciddi şekilde farklı tasarımlara sahip olabilir.
-   Şablonları geliştirici/coding agent kod tarafında geliştirir.
-   Kullanıcı veya Super Admin'in HTML/CSS yazarak şablon oluşturduğu
    bir CMS bulunmaz.
-   React tarafında güvenli template renderer/component'ları kullanılır.
-   DB'de template key, isim, kategori, aktif/pasif, ücretsiz/premium,
    preview, desteklenen modüller, gerekli ve önerilen alanlar gibi
    metadata tutulur ve yönetilebilir.
-   Şablonlar public `/sablonlar` sayfasından üyelik olmadan
    incelenebilir.
-   Demo verileriyle şablon önizlemesi yapılabilir.
-   Creator kendi gerçek bilgileriyle preview yapabilir.
-   Şablonların gelişimi mevcut yayınlanmış davetiyeleri bozmayacak
    şekilde ele alınır.
-   Template renderer sözleşmesi değiştirilebilir ve genişletilebilir
    olmalıdır. Yeni tasarımlar veya mevcut tasarımların yeniden
    geliştirilmesi sistem mimarisinin yeniden yazılmasını gerektirmez.

## 6. Responsive Tasarım

Ayrı mobil ve masaüstü davetiyesi geliştirilmeyecek. **Tek web
uygulaması ve tek davetiye URL'si** kullanılacak.

Telefon, tablet, laptop ve masaüstünde responsive çalışacak. Bu kural
public davetiye, Creator Panel ve Super Admin Panel için geçerlidir.

Creator editöründe: - Telefon önizleme - Tablet önizleme - Masaüstü
önizleme - Yeni sekmede tam önizleme

bulunur.

Preview ve gerçek public davetiye mümkün olduğunca aynı renderer'ı
kullanır.

## 7. Public Davetiye

Örnek URL:

`/davetiye/hande-fatih-k7m4qx`

Burada okunabilir bölüm slug, sondaki random değer ise çakışmaları
engelleyen public code'dur. Public code şifre veya yönetim token'ı
değildir.

Linki bilen davetiyeyi görebilir. Kişiye özel Ahmet/Mehmet linkleri
MVP'de bulunmaz ve ilerisi için backlog'a bırakılır.

Public davetiyelerin arama motorlarında varsayılan olarak indexlenmemesi
tercih edilir.

## 8. Davetiye İçeriği ve Modüller

Şablona ve Creator'ın seçimine göre şu modüller bulunabilir:

-   Ana/hero alanı
-   İsimler veya etkinlik başlığı
-   Tarih
-   Kısa davet mesajı
-   Kapak fotoğrafı/video
-   Tarih ve mekan
-   Opsiyonel interaktif harita
-   Google Maps / Apple Maps yönlendirmesi
-   Geri sayım
-   Takvime ekleme
-   Program / etkinlik akışı
-   Fotoğraf/video galerisi
-   RSVP
-   İletişim
-   Anılarımız
-   Hediye/Çeyiz listesi
-   Duyurular
-   FAQ
-   Ulaşım/servis bilgileri
-   QR kod

Tek davetiye bir ana etkinliği temsil eder. Program içinde birden fazla
aşama bulunabilir.

Örnek:

-   18:00 Karşılama
-   19:00 Nikâh
-   20:00 Yemek
-   21:30 İlk Dans

## 9. Fotoğraf ve Video

Creator davetiyesine fotoğraf ve video yükleyebilir ve galeri
oluşturabilir.

Creator fotoğraf yüklemelerinden EXIF/GPS metadata, dosya sağlayıcı
depolamasına aktarılmadan önce kaldırılır. Creator fotoğraf ve videoları
cover ve gallery sunumlarında kullanabilir.

Fotoğraf/video dosyaları VPS diskinde veya PostgreSQL içinde tutulmaz.
Fotoğrafların object storage katmanında **Cloudflare R2**, responsive
teslimat ve dönüşümlerinde **Cloudflare Images Transformations**;
videolarda **Cloudflare Stream** kullanılır.

Yüklemelerde mümkün olduğunca direct upload yaklaşımı kullanılır. API,
yükleme öncesinde sahiplik, modül durumu, paket hakkı, dosya boyutu ve
güvenlik kontrollerini yaparak kısa ömürlü upload yetkisi üretir.

DB yalnızca medya metadata'sını, sahiplik bilgisini, key/URL bilgisini,
türünü, boyutunu ve durumunu tutar.

Fotoğraf/video adet, boyut ve uygun olduğunda video süre limitleri
paketlere göre değişir ve DB-driven entitlement olarak yönetilir.
Creator medya kotası Guest medya kotasından ayrıdır; Guest medya kendi
entitlement ve abuse sınırlarına tabidir.

Creator medya upload intent'i yalnızca publication window/grant atanmış bir
davetiye için oluşturulabilir; grant atanmamış Draft davetiyeye medya
yüklenemez. Cover/gallery sunumu seçilirken şablonun ilgili modülü
desteklemesi yeterlidir; Creator tarafından ayrı bir modül etkinleştirme
alanı gerekmemektedir.

## 10. RSVP

Creator isterse RSVP modülünü açabilir. Modül ilk açıldığında şu
başlangıç soru seti oluşturulur:

-   Adınız
-   Katılacak mısınız?
-   Kaç kişi katılacaksınız?
-   Notunuz / mesajınız (opsiyonel)

Creator varsayılan soruları değiştirebilir, silebilir, yeniden
sıralayabilir, zorunlu/opsiyonel yapabilir ve yeni soru ekleyebilir.

Fatih 2026-10-05'te varsayılan zorunlulukları onayladı: ad, katılım ve kişi
sayısı soruları zorunludur; not/mesaj sorusu isteğe bağlıdır.

Fatih 2026-10-05'te RSVP soru yapılandırmasının yalnız Taslak veya
Duraklatıldı davetiyelerde düzenlenmesine karar verdi. Active davetiyede soru
değişiklikleri engellenir; Creator önce davetiyeyi duraklatmalıdır.

Fatih 2026-10-05'te RSVP yanıt/doğrulama sınırlarını makul engineering
varsayımlarıyla belirleme yetkisi verdi. Başlangıç sınırları: kısa yanıt en çok
200, uzun yanıt en çok 2.000 karakter; semantic ParticipantCount 0–20 arasında
tam sayı; MultipleChoice yanıtında en çok 10 seçili seçenek; davetiye başına
en çok 20 aktif soru. `0`, RSVP'ye katılmayacağını bildiren misafirin
katılımcı sayısını tutarlı biçimde yanıtlamasını sağlar. Semantic rolü olmayan
Number soruları decimal kabul eder ve .NET decimal temsil aralığıyla sınırlıdır.
Kaynak tüketimini sınırlandırmak için soru prompt'u en çok 200 karakter,
seçenek etiketi en çok 100 karakter ve her choice sorusu en çok 20 seçenek
olabilir. Bu değerler backend doğrulamasında ve Creator arayüzünde tutarlı
uygulanır; planın `maxRSVPResponses` kotasından ayrıdır.

Desteklenebilecek soru türleri: - Kısa metin - Uzun metin - Tek seçim -
Çoklu seçim - Evet/Hayır - Sayı

RSVP cevapları diğer davetlilere **kesinlikle gösterilmez**. Creator
kendi davetiyesinin sonuçlarını görür.

Kişiye özel davetli hesabı olmadığı için aynı kişinin farklı cihazlardan
tekrar cevap vermesi tamamen engellenemez.

Aynı tarayıcıda anonim token/cookie yardımıyla **Daha önce yanıt
verdiniz → Yanıtımı Güncelle** akışı sağlanabilir.

Farklı cihaz, gizli sekme veya cookie temizleme durumlarında mükerrer
cevap oluşabilmesi bilinen MVP kısıtıdır. Creator mükerrer kayıtları
yönetebilir. Fatih 2026-10-05'te Creator için yalnızca mükerrer yanıtı
silme davranışını seçti; yok sayma ve birleştirme MVP kapsamı dışındadır.

Toplam katılımcı istatistiği, "Kaç kişi katılacaksınız?" sorusunun
cevaplarından üretilebilir. Creator bu soruyu değiştirir veya silerse
istatistiğin nasıl etkileneceği arayüzde açıkça belirtilir.

## 11. İletişim

Sistem gelin/damat gibi rollere sabitlenmez.

Creator birden fazla iletişim kişisi ekleyebilir. Her kayıt isim,
opsiyonel rol/açıklama, telefon ve uygun iletişim aksiyonlarını
içerebilir.

## 12. Anılarımız

Bu bölüm Creator'ın kendi galerisinden ayrıdır.

Davetliler: - mesaj, - emoji, - fotoğraf, - video

bırakabilir.

Varsayılan görünürlük **Sadece Creator**'dır. Creator **Public** seçerse
misafir mesajı ve emojisi hemen, fotoğraf/video ise yalnızca provider
tarafından doğrulanıp `Ready` olduktan sonra herkese görünür. Public'den
Sadece Creator'a geçiş mevcut anıları da public görünümden kaldırır; veri
silinmez ve ayar yeniden Public yapılırsa görünürlük geri döner.

Creator anıları gizleyebilir veya silebilir. Gizleme terminaldir; gizlenen anı
yeniden public yapılamaz ancak Creator silebilir. Creator listesi Published ve
Hidden anıları içerir; PendingMedia ve Abandoned kayıtlarını içermez.

Memories entitlement'ı veya davetiye modülü kapatılırsa mevcut anılar public
görünümden saklanır ve korunur; Creator bunları görüp silebilir, yeni anı
kabul edilmez. Memories modülü davetiye başına Creator tarafından açılır ve
varsayılan olarak kapalıdır. Guest gönderimi yalnızca davetiye effective Active
iken kabul edilir; Scheduled davetiye anı kabul etmez.

Memory bırakmak için ad, e-posta veya telefon zorunlu değildir. Guest
isterse görünen bir isim yazabilir veya anonim bırakabilir.

Misafir uploadlarında dosya türü/boyutu kontrolleri, güvenlik kontrolleri ve
rate limit uygulanır. Başlangıç plan kataloğu değerleri davetiye başına 100
fotoğraf ve 10 video; fotoğraf başına 10 MB; video başına 100 MB ve 60 saniyedir.
Guest medya kotası Creator medya kotasından ayrıdır. Fotoğraflarda EXIF/GPS
metadata provider depolamasına yazılmadan önce Worker normalizasyonuyla
temizlenir; guest videolarında metadata temizliği MVP'de garanti edilmez.

## 13. Hediye / Çeyiz Listesi

Altyapıda genel bir **Gift Registry** mantığı kullanılır. Görünen başlık
etkinlik türüne göre değişebilir.

Creator ürün ve istenen miktarı ekler. Davetliler ürünleri rezerve
edebilir. Partial quantity reservation desteklenir; örneğin altı adet
istenen bir üründen iki adet rezerve edilebilir.

Rezervasyon sırasında guest ad-soyadı zorunludur; e-posta ve telefon
birbirinden bağımsız olarak opsiyoneldir (misafir ikisini, birini veya
hiçbirini girebilir). Creator rezervasyonu yapan kişinin ad-soyadını ve
verilmişse iletişim bilgisini görebilir. İletişim bilgisi Guest iptali veya
Creator'ın rezervasyonu kaldırmasıyla silinir; rezervasyon aktif kaldığı
sürece davetiye kalıcı silinene kadar saklanır.

Diğer davetliler rezervasyonu yapan kişinin kimliğini görmez; yalnızca
ürünün seçildiğini veya miktar ilerlemesini görür.

Creator kimin neyi seçtiğini görebilir ve gerektiğinde rezervasyonu
kaldırabilir. Fatih 2026-10-05'te karar verdi: aktif Guest rezervasyonu
bulunan bir item silinemez; Creator önce rezervasyonları ayrı ayrı
kaldırmalıdır. Her kaldırma tüm rezervasyon kaydını ve kimliği siler, miktarı
yeniden rezerve edilebilir hale getirir. Item ancak aktif rezervasyonu
kalmadığında silinebilir. Rezervasyon otomatik expire olmaz. Guest aynı browser'da
saklanan anonim management token/cookie ile kendi rezervasyonunu iptal
edebilir.

Misafir rezervasyonu iptal ettiğinde veya Creator rezervasyonu kaldırdığında
rezervasyon kaydının tamamı (ad-soyad ve varsa e-posta/telefon dahil) silinir;
miktar tekrar rezerve edilebilir.

`giftRegistryEnabled` entitlement'ı kapanırsa mevcut hediye listesi
misafirlerden gizlenir ve yeni rezervasyonlar durur. Hediye item ve
rezervasyon verileri korunur; Creator listeyi görüp yönetebilir. Entitlement
yeniden açılırsa liste tekrar public olur.

## 14. Duyuru, FAQ ve Ulaşım

Creator yayınlanmış davetiyeye sonradan duyuru ekleyebilir.

FAQ bölümünde otopark, çocuk kabulü gibi sık sorulan sorular
bulunabilir.

Ulaşım/servis bölümünde kalkış noktaları ve saatleri gösterilebilir.

Bu bölümler modüler ve açılıp kapatılabilir yapıdadır.

## 15. QR ve Open Graph

Her davetiye public URL'ye yönlenen QR koduna sahip olabilir.

WhatsApp ve diğer sosyal uygulamalarda link paylaşımında davetiyenin
başlığı, görseli ve uygun özet bilgileri Open Graph metadata ile düzgün
gösterilir.

## 16. Paylaşım

Public davetiye içerisinde ayrıca bir paylaş butonu bulunmaz.

Paylaşım Creator Panel üzerinden yapılır:

-   Linki Kopyala
-   WhatsApp'ta Paylaş
-   Cihazın native paylaşım özelliği
-   QR Kodunu Göster/İndir

## 17. Yayınlama ve Durumlar

Temel yaşam döngüsü:

-   Hemen yayınlama: `Draft → Active`
-   Planlı yayınlama: `Draft → Scheduled → Active`
-   Geçici durdurma: `Active ↔ Paused`
-   Hak süresi bitişi: `Active/Paused → Expired`

Creator normal yayınlama akışında **Yayınla** dediğinde davetiye hemen
Active olur. İsterse yayın tarihini planlama seçeneğini açarak başlangıç
tarih/saatini belirleyebilir; bu durumda davetiye başlangıç zamanına
kadar Scheduled durumunda tutulur ve zamanı geldiğinde Active olur.

Creator gerekirse yayın bitiş tarih/saatini de seçebilir. Seçilen bitiş
zamanı paket hakkının izin verdiği maksimum yayın süresini aşamaz.
Creator aktif davetiyesini pasife alabilir ve hakkı devam ettiği sürece
tekrar aktifleştirebilir.

Gerçekten gerekli ve şablonun düzgün çalışması için zorunlu bir alan
eksikse yayınlamaya izin verilmez.

Opsiyonel/recommended alanlar eksikse sistem uyarı verir ancak Creator
**Yine de Yayınla** diyebilir.

Validation şablonun ihtiyaçlarına göre çalışabilir.

Expired davetiye yeni ve uygun bir yayın hakkı/paket alındığında tekrar
aktif edilebilir. Aynı invitation kaydı, içerik ve public URL korunur.

## 18. Yayındaki Davetiyeyi Düzenleme

Ayrı bir içerik sürüm/version sistemi kullanılmaz.

Creator yayındaki davetiyede değişiklik yapıp **Güncelle** dediğinde
değişiklik public davetiyeye doğrudan yansır.

Her tuş vuruşu canlıya gönderilmez.

### Şablon Değiştirme İstisnası

Yayındaki davetiyenin şablonu doğrudan değiştirilemez.

Akış:

`Active → Paused → Şablonu değiştir → Preview → Tekrar Active`

Kullanıcıya şablon değiştirebilmek için davetiyeyi geçici olarak pasife
alması gerektiği açıkça belirtilir.

## 19. Yayın Süresi ve Paketler

Bireysel kullanıcılar için tek seferlik **yayın hakkı**, Organization
hesapları için abonelik tabanlı paket yaklaşımı kullanılır.

Yayın hakkı takvim bazlıdır. Süre, ilk aktivasyon veya planlanmış
başlangıç zamanı ile işlemeye başlar. Pause işlemi süreyi durdurmaz,
uzatmaz veya biriktirmez. Örneğin 30 günlük yayın hakkı başladıktan
sonra davetiye pause edilse bile hak bitiş tarihi değişmez.

Başlangıç DB seed değerleri aşağıdadır. Bunlar kod sabiti değildir;
Super Admin tarafından değiştirilebilen başlangıç ticari değerleridir.
Liste fiyatlarının vergi/gösterim biçimi production öncesinde ticari ve
hukuki olarak doğrulanır.
MVP'de paket fiyatı tutar ve ödeme dönemiyle gösterilir (ör. `1.199 TRY / tek
sefer`); verginin fiyata dahil veya hariç olduğu iddiası gösterilmez. Bu
iddia Phase 11'de hukuki/ticari doğrulamaya tabidir.

Organization aboneliğinin paid-through süresi dolduğunda public erişim ve
aktif yayın durur; davetiye ve içerik verisi korunur. Uygun bir grant
atanırsa davetiye yeniden yayınlanabilir.
Organization aboneliği, Creator iptal edene kadar aylık olarak otomatik
yenilenir ve tahsil edilir.
Creator iptal ettiğinde bir sonraki yenileme durur; mevcut paid-through
döneminin sonuna kadar yayın ve public erişim sürer. Dönem sonunda yukarıdaki
erişim kesme ve veri koruma kuralı uygulanır.
İptal işlemi tamamlandığında Creator'ın hesap e-posta adresine iptal onayı
gönderilir.

Standard ve Premium tek seferlik paketlerde iade talepleri destek üzerinden
manuel değerlendirilir. MVP'de Creator için uygulama içi veya otomatik iade
akışı bulunmaz; kesin yasal iade koşulları ve metinleri Phase 11'de doğrulanır.
Destek kararıyla işlenen tam iade sağlayıcı tarafından doğrulandığında ilgili
yayın hakkı iptal edilir, davetiye yayından kaldırılır ve içerik verisi korunur.
Kart itirazı (chargeback) kesin olarak kaybedilirse aynı erişim sonucu uygulanır;
itiraz incelemesi sürerken hak ve yayın durumu değişmez.
Creator ödeme sayfasından vazgeçerse yeni bir ödeme denemesi başlatabilir.
İptal edilen eski deneme yayın hakkı oluşturmaz; hak yalnızca sunucu tarafında
doğrulanmış başarılı ödeme sonrasında açılır.
Ödeme sağlayıcısı tek seferlik paket denemesini başarısız döndürürse Creator
yeni bir deneme başlatabilir; başarısız deneme yayın hakkı oluşturmaz.
Ödeme başlatma butonunun metni `Ödemeye geç` olur.

| Özellik | Free | Standard | Premium | Organization |
| --- | ---: | ---: | ---: | ---: |
| Başlangıç fiyatı | 0 TRY | 699 TRY / tek sefer | 1.199 TRY / tek sefer | 2.499 TRY / ay |
| `maxPublishDays` | 1 | 30 | 90 | 365 |
| `maxActiveInvitations` | 1 | 1 | 1 | 10 |
| `maxImages` (davetiye başına) | 5 | 30 | 100 | 250 |
| `maxVideos` (davetiye başına) | 0 | 1 | 5 | 10 |
| `maxImageSizeMb` | 5 | 10 | 10 | 10 |
| `maxVideoSizeMb` | 0 | 250 | 500 | 1.000 |
| `maxVideoDurationSeconds` | 0 | 180 | 600 | 900 |
| `maxRSVPResponses` (davetiye başına) | 50 | 300 | 1.000 | 5.000 |
| `memoriesEnabled` | Hayır | Evet | Evet | Evet |
| `giftRegistryEnabled` | Hayır | Evet | Evet | Evet |
| `premiumTemplatesEnabled` | Hayır | Hayır | Evet | Evet |

Bu değerler lansman öncesinde Super Admin üzerinden değiştirilebilir;
application release veya deploy gerektirmez.

Creator paketinin izin verdiği maksimum süre içinde yayın tarihlerini
belirleyebilir. Organization için ileride 10/50/unlimited gibi farklı
paketler tanımlanabilir; MVP başlangıç paketi aynı anda en fazla 10 aktif
davetiyeye izin verir.

## 20. DB-Driven Paket Sistemi

Paket özellikleri kod içine gömülmez.

Örnek özellikler:

-   `maxPublishDays`
-   `maxImages`
-   `maxVideos`
-   `maxImageSizeMb`
-   `maxVideoSizeMb`
-   `maxVideoDurationSeconds`
-   `maxActiveInvitations`
-   `maxRSVPResponses`
-   `memoriesEnabled`
-   `giftRegistryEnabled`
-   `premiumTemplatesEnabled`

Paket limitleri mümkün olduğunca deploy gerektirmeden DB'den
değiştirilebilir. Plans/PlanFeatures veya eşdeğer typed entitlement
yapısı kullanılır. Plan ve entitlement değerleri Super Admin UI
üzerinden; gerektiğinde doğrudan DB operasyonuyla yönetilebilir.

Plan değişiklikleri mevcut kullanıcılara da uygulanabilir. Limit
düşürülmesi mevcut kullanıcı verisini otomatik olarak silmez. Mevcut
kullanım yeni limitin üzerindeyse içerik korunur; kullanım yeniden limit
altına düşene kadar yeni içerik eklenmesine izin verilmez.

## 21. DB-Driven Sistem Ayarları

Değişmesi muhtemel global iş kuralları mümkün olduğunca DB üzerinden
yönetilir.

Örnek:

`deletedInvitationRetentionDays = 3`

Dosya limitleri, varsayılan dil/para birimi ve benzeri ayarlar da uygun
olduğunda bu sistemde tutulabilir.

Güvenlik açısından kritik alanlarda backend ayrıca **hard ceiling**
uygular. DB'ye hatalı veya aşırı değer girilmesi sistemi tehlikeye
atmamalıdır.

## 22. Silme ve Çöp Kutusu

Creator kendi davetiyesini silebilir.

Silinen davetiye hemen fiziksel olarak kaldırılmaz. Varsayılan olarak
**3 gün çöp kutusunda** tutulur.

Creator bu sürede geri yükleyebilir.

Retention süresi DB'den değiştirilebilir.

Çöp kutusundaki davetiye: - Public olarak erişilemez. - Aktif davetiye
kotasından sayılmaz.

Retention süresi sonrasında kalıcı silme süreci uygulanır.

Süresi dolan, reddedilen veya tamamlanmamış medya yüklemeleri sağlayıcıdaki
byte'larıyla davetiye kalıcı olarak silinene kadar tutulur. Yükleme intent'inin
expire/reject olması erişimi ve devam eden capability'yi kapatır; tek başına
fiziksel provider silme başlatmaz. Kalıcı purge sırasında davetiyeye bağlı tüm
medya durumları idempotent provider-delete kuyruğuna alınır.

Provider'dan silinmesi henüz doğrulanmamış `Rejected` ve `PendingDeletion`
Creator medyası, davetiyenin mevcut Creator medya öğesi kotasını kullanmaya
devam eder. Yalnız provider silmesinin doğrulandığı `Deleted` öğe kotayı
bırakır; mantıksal silme veya reddedilme, saklanan dosya için yeni bir boş
quota slotu oluşturmaz.

Kalıcı purge, invitation'a bağlı aşağıdaki verileri ve diğer davetiye
içeriklerini temizler:

-   RSVP soru ve cevapları
-   Memories kayıtları
-   Guest media
-   Creator invitation media
-   Gift item ve reservation kayıtları
-   Invitation istatistikleri
-   Cloudflare üzerindeki ilişkili medya

Payment/invoice kayıtları ve gerekli audit kayıtları operasyonel veya
yasal retention gereklerine göre invitation content'ten ayrıştırılarak
daha uzun süre tutulabilir.

Creator hesabı silme talebini başlatır ve e-posta doğrulamasıyla onaylar.
Doğrulama sonrası aktif oturumlar hemen iptal edilir; hesap ve hesaba ait tüm
davetiyeler ile konuk içerikleri çöp kutusu bekleme süresi olmadan kalıcı
silme akışına girer ve public erişim anında kapanır. Cloudflare medya
silmesi idempotent, tekrar denenebilir bir provider işlemi olarak tamamlanır.
Hukuken saklanması gereken ödeme ve audit kayıtları içerik silmesinden ayrı
tutulur. Kesin yasal/operasyonel retention süreleri Phase 11 hukuk incelemesine
kadar belirlenmemiştir; o zamana kadar ödeme, audit, log ve backup verileri
otomatik silinmez. Hesap silme isteği otomatik refund başlatmaz; ödeme
değerlendirmesi destek üzerinden manuel yapılır. Silme başladıktan sonra
önceden başlatılmış tek seferlik ödeme sağlayıcıda başarılı sonuçlanırsa
settlement kanıtı saklanır fakat silinmiş hesaba yeni yayın hakkı verilmez;
olası refund talebini destek manuel değerlendirir.

## 23. Planlanmış, Süresi Bitmiş veya Pasif Davetiye

Davetiyenin planlanmış başlangıç zamanı henüz gelmediyse, süresi
bittiyse veya davetiye pasife alındıysa public içerik gösterilmez.

Genel bir durum sayfası gösterilir:

> Bu davetiye şu anda yayında değil. Yayın henüz başlamamış, yayın
> süresi sona ermiş veya davetiye geçici olarak pasife alınmış olabilir.

İsim, fotoğraf, tarih gibi özel bilgiler gösterilmez.

Silinmiş veya hiç var olmamış URL normal 404 dönebilir.

## 24. Creator İstatistikleri

Creator kendi davetiyesi için bilgi/istatistik alanından genel verileri
görebilir:

-   Toplam sayfa görüntüleme
-   RSVP cevapları
-   Toplam katılım sayısı
-   Anı sayısı
-   Medya sayısı
-   Hediye rezervasyonları

MVP'de anonymous visitor ID/cookie ile unique visitor tracking yapılmaz.
Amaç kişileri izlemek değil, davetiyenin genel durumunu aggregate
istatistiklerle göstermektir. Kişiye özel davetli linkleri ileride
eklenirse gelişmiş visitor/guest analytics ayrıca tasarlanır.

## 25. Super Admin

Super Admin platform seviyesinde yönetim yapar.

Örnek genel veriler: - Toplam kullanıcı - Bireysel/organizasyon
hesapları - Toplam davetiye - Aktif/pasif/expired davetiyeler - Paket
kullanımları - Abonelikler - Ödemeler - Genel depolama kullanımı -
Sistem durumu

Super Admin normal şartlarda kullanıcıların özel RSVP/anı/hediye
içerikleriyle ilgilenmez.

### Ban Sistemi

Super Admin kullanıcıyı banlayabilir.

Banlanan kullanıcı: - Giriş yapamaz. - Yeni davetiye oluşturamaz. -
Aktif davetiyeleri yayından kaldırılır.

Ban kaydında sebep, tarih, işlemi yapan Super Admin ve dahili not gibi
bilgiler tutulabilir.

Önemli Super Admin işlemleri audit log'a yazılır.

MVP'de MFA doğrulaması tamamlanmış Super Admin aktif banı kaldırabilir.
Unban işlemi audit log'a yazılır ve hesabın erişimi geri gelir.

Super Admin public registration üzerinden oluşturulmaz; kontrollü bir
bootstrap yöntemi kullanılır. Super Admin hesaplarında MFA zorunludur.
Creator hesaplarında MFA MVP'de zorunlu değildir.

MVP'de Admin, banlı hesap listesinde Creator e-posta adresini görebilir ve
e-posta önekine göre arayabilir. Bu görünüm dahili ban notunu ve özel davetli
içeriklerini içermez.

## 26. E-posta

Ürün ilk etapta bir **web application** olacaktır.

Native mobil uygulama, SMS, WhatsApp notification ve mobile push
notification MVP kapsamında değildir.

E-posta şu işlemlerde kullanılabilir: - E-posta doğrulama - Şifre
sıfırlama - Ödeme/paket işlemleri - Davetiye yayın işlemleri - Yayın
süresinin bitmesine yaklaşılması - Kritik hesap/güvenlik bildirimleri

Standard/Premium tek seferlik paket ödemesi başarılı olduğunda Creator'ın
hesap e-posta adresine satın alma onayı gönderilir.
Standard/Premium tek seferlik paket ödemesi başarısız olduğunda Creator'ın
hesap e-posta adresine bildirim gönderilir. Aktif bireysel yayın hakkı
bitmeden 7 gün önce Creator'a bitiş hatırlatma e-postası da gönderilir.
Organization aboneliğinin aylık yenileme tahsilatı başarısız olduğunda
Creator'ın hesap e-posta adresine, aylık fatura dönemi başına ilk başarısız
deneme için bir kez bildirim gönderilir.
Organization aboneliğinin her başarılı aylık yenileme tahsilatında Creator'ın
hesap e-posta adresine ödeme onayı gönderilir.
Başarısız yenileme tahsilatı, mevcut paid-through dönem bitmeden önce
provider'ın yapılandırılmış otomatik tekrar politikasıyla yeniden denenir;
gerçek merchant retry ayarları Phase 11'de doğrulanır.
Organization aboneliğini iptal etmiş Creator'a, mevcut paid-through dönemi
bitmeden 7 gün önce erişimin sona ereceğini hatırlatan e-posta gönderilir.

Her RSVP/anı geldiğinde ayrı e-posta göndererek spam oluşturulmaz.
Creator'ın davetiyesi başarıyla yayınlandığında hesap e-posta adresine yayın
onayı gönderilir.

Production transactional email provider **Resend**'dir. Provider
abstraction korunur (`IEmailSender` veya eşdeğeri). Development
ortamında fake/local sender kullanılabilir. Provider değişikliği domain
ve application katmanlarını etkilemez.

## 27. Dil ve Para Birimi

İlk sürüm:

-   **Türkçe**
-   **TRY**

Kutlio arayüzü Türkçe ve İngilizce sunulur. Türkçe varsayılandır; Creator ve
Super Admin dil seçimi hesap tercihlerine kaydedilir ve farklı cihazlarda
korunur. Bu tercih uygulamanın kendi arayüz metinlerini ve davetli
etkileşimlerindeki Kutlio metinlerini değiştirir. Davetiye sahibinin girdiği
başlıklar, mesajlar ve özel alan etiketleri olduğu gibi gösterilir. Mevcut
hukuki metinler ve e-posta şablonları bu arayüz dili tercihinin kapsamına
girmez.

Giriş yapan kullanıcılar görünümünü beş renk paletinden seçebilir: **Kutlio**,
**Adaçayı**, **Gül**, **Okyanus** ve **Erik**. Açık/koyu görünüm tercihi bu
paletlerden bağımsızdır ve **Cihaz ayarı**, **Açık** veya **Koyu** olarak
seçilebilir. Varsayılan görünüm Kutlio paleti ve cihaz ayarıdır. Palet ve
görünüm tercihleri de hesapta saklanır.

Giriş sonrası Creator ve Super Admin alanları rolüne uygun ana navigasyon
gösterir. Her iki alanda dil, palet ve görünüm tercihleri sağ üstteki hesap
çekmecesinden; Creator ayrıca Hesap tercihleri sayfasından yönetilebilir.
Güvenli çıkış hesap çekmecesinde yer alır.

Frontend i18n uyumlu, backend/veri modeli de genişletilebilir olmalıdır.

## 28. Teknik Temel

-   **Frontend:** React
-   **Backend:** ASP.NET Core Web API / .NET
-   **Database:** PostgreSQL
-   **Version Control:** Git
-   **Sunucu:** Hostinger VPS
-   **Fotoğraf:** Cloudflare R2 + Cloudflare Images Transformations
-   **Video:** Cloudflare Stream
-   **Transactional e-posta:** Resend
-   **Production ödeme:** iyzico
-   **Deployment:** Git push sonrasında otomatik build/deploy hedefi
-   **Hosting:** Hostinger VPS + Docker Compose + Nginx

Authentication ve authorization backend tarafından güvenli şekilde
uygulanır.

Tercihen Secure + HttpOnly + SameSite cookie tabanlı oturum yaklaşımı
kullanılır. Auth credential'ları plain localStorage içinde tutulmaz.

Public davetiye kodu hiçbir zaman Creator/Admin yetkisi sağlamaz.

Ödeme katmanı provider abstraction üzerinden tasarlanır
(`IPaymentGateway` veya eşdeğeri). Merchant hesabı ve credentials hazır
olana kadar development ortamında `FakePaymentGateway` kullanılabilir.
Production'da `IyzicoPaymentGateway` devreye alınır. Webhook signature,
idempotency ve başarılı ödeme sonrasında entitlement activation
altyapısı provider-bağımsız olur. Merchant secret/API key değerleri
repository'ye yazılmaz.

Production domain henüz belirlenmemiştir. İlk geliştirme ve deployment
IP üzerinden çalışabilmelidir; domain application code içine hardcode
edilmez. Domain satın alındığında Nginx, HTTPS ve public base URL
configuration ile eklenir. Production Google login'in etkinleştirilmesi
için doğrulanabilir domain ve HTTPS zorunlu deployment önkoşuludur.

Production secret'ları Git repository'ye girmez. Başlangıçta günlük
off-site PostgreSQL backup yeterlidir. Sistem büyüdüğünde ve RPO/RTO
ihtiyacı gerektirdiğinde PITR ayrıca değerlendirilir.

## 29. KVKK / Privacy

Mimari baştan KVKK ve privacy gereksinimleri düşünülerek tasarlanır.

-   Data minimization uygulanır; özellik için gerekmeyen kişisel veri
    toplanmaz.
-   Hizmetin çalışması için gerekli consent ile marketing consent ayrı
    tutulur. Hizmet bildirimi zorunludur; marketing izni ayrı ve varsayılan
    kapalı opt-in olarak alınır. MVP'de isteğe bağlı çerez/izleme yoktur.
    Kesin metinlerin hukuki yeterliliği Phase 11 hukuk incelemesine tabidir.
-   Account/data deletion taleplerini destekleyecek veri sahipliği ve
    silme altyapısı bulunur. E-posta doğrulamalı hesap silme onayı oturumları
    hemen kapatır ve hesap/içerik purge'unu başlatır.
-   Retention politikaları uygun alanlarda configurable olur.
-   Günlük off-site backup için MVP kurtarma hedefi RPO ≤24 saat ve RTO ≤8
    saattir. Gerçek restore provası ve VPS kapasite doğrulaması Phase 11
    operasyon kapısıdır.
-   Gelecekte data export eklenebilmesine uygun veri sınırları korunur;
    data export özelliğinin kendisi MVP kapsamına eklenmez.
-   Legal metinler AI tarafından hukuken kesin kabul edilmez ve
    production öncesinde yetkin kişilerce doğrulanır.

## 30. Temel Ürün Prensibi

> **Bu bir tasarım editörü değil, hızlı davetiye oluşturma
> platformudur.**

Ana deneyim:

`Şablonunu seç → Bilgilerini gir → İstediğin özellikleri aç → Önizle → Yayınla`

Kullanıcı gereksiz seçeneklerle boğulmaz. Gelişmiş özellikler daha sonra
Creator Panel'den eklenebilir.

## 30a. Ana Sayfa (Tanıtım Sayfası)

Fatih'in 2026-10-08 kararı. Ürün markası **Kutlio**'dur; alan adı
`kutlio.com`. `/` adresi herkese açık, hesap gerektirmeyen bir tanıtım
sayfasıdır. Bölümler bu sırayla:

1. **Hero:** başlık `Davetiyeni dakikalar içinde hazırla`; alt metin
   `Şablonunu seç, bilgilerini gir, tek linkle tüm davetlilerine ulaştır.`
   Birincil buton `Ücretsiz başla` → kayıt (`/giris/kayit`); ikincil buton
   `Şablonları incele` → `/sablonlar`.
2. **Nasıl çalışır:** üç adım — şablon seç, bilgilerini doldur, linkle
   paylaş (§1, §4, §16).
3. **Şablon vitrini:** canlı şablon kataloğundan birkaç aktif şablon kartı ve
   tüm şablonlara bağlantı. Şablonlar sayfaya sabit yazılmaz.
4. **Özellikler:** yalnız gerçekten çalışan özellikler tanıtılır — RSVP ve
   katılım takibi, Anılarımız, hediye/çeyiz listesi, QR ve paylaşım, takvime
   ekleme/geri sayım/harita, mobil uyum, davetlinin hesap açmaması.
   Fotoğraf/video yükleme, production medya sağlayıcısı açılana kadar
   tanıtılmaz.
5. **Paketler:** aktif paketlerin (Free, Standard, Premium, Organization)
   kartları. Fiyat, ödeme dönemi ve temel limitler sayfaya sabit yazılmaz;
   Super Admin'in yönettiği canlı paket verisinden gelir (§19, §20). Fiyat
   tutar ve dönemle gösterilir; vergi dahil/hariç iddiası yoktur. Kart
   butonu kayda gider; ödeme ana sayfadan başlatılmaz.
6. **SSS:** cevapları bu belgeden türetilir; yayına açılmadan önce metinleri
   Fatih onaylar.
7. **Alt bilgi:** Gizlilik, Kullanım koşulları ve `destek@kutlio.com`.

Ana sayfa arama motorlarına açıktır (davetiye sayfaları `noindex` kalır);
başlık ve açıklama Kutlio markasını taşır. Responsive ve erişilebilirlik
kuralları diğer public sayfalarla aynıdır.

## 31. Backlog / MVP Dışı

İlk sürümde özellikle kapsam dışında bırakılan özellikler:

-   Kişiye özel davetli linkleri
-   Binlerce kişilik önceden tanımlı davetli listesi/import sistemi
-   "Kim linki açtı?" kişi bazlı takip
-   Native mobil uygulama
-   SMS bildirim sistemi
-   WhatsApp bildirim otomasyonu
-   Mobile push notification
-   Kapsamlı içerik sürüm geçmişi
-   Drag-and-drop tasarım editörü
-   Kullanıcıların kendi template kodlarını oluşturması
-   Davetiye çoğaltma/kopyalama sistemi

Bu özellikler ileride değerlendirilebilir ancak MVP'nin parçası
değildir.

------------------------------------------------------------------------

## Not

Bu belge şu ana kadar yapılan ürün keşfi konuşmalarının kapsam özetidir.
**Agent geliştirme promptu değildir.** Kabul edilmiş teknik kararların
özeti `docs/ARCHITECTURE.md`, VPS ve operasyon kuralları
`docs/DEPLOYMENT.md` içinde tutulur. Bir uygulama kararı bu belgeyle
çelişemez.
