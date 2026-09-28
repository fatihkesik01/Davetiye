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
-   E-posta + şifre temel giriş yöntemidir.
-   Google ile giriş desteklenebilir.
-   E-posta doğrulama, şifre sıfırlama ve güvenli oturum yönetimi
    bulunur.
-   Telefon/SMS altyapısı ileride kullanılabilecek şekilde düşünülebilir
    ancak MVP bağımlılığı değildir.
-   Bireysel ve organizasyon hesabı ayrımı bulunur.
-   Organizasyon hesapları çok sayıda müşteri davetiyesi yönetebilir.

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

-   İlk etapta yaklaşık **5--10 kaliteli şablon** oluşturulur.
-   Şablonlar ciddi şekilde farklı tasarımlara sahip olabilir.
-   Şablonları geliştirici/coding agent kod tarafında geliştirir.
-   Kullanıcı veya Super Admin'in HTML/CSS yazarak şablon oluşturduğu
    bir CMS bulunmaz.
-   React tarafında güvenli template renderer/component'ları kullanılır.
-   DB'de template key, isim, kategori, aktif/pasif, ücretsiz/premium,
    preview, desteklenen modüller, gerekli ve önerilen alanlar gibi
    metadata tutulabilir.
-   Şablonlar public `/sablonlar` sayfasından üyelik olmadan
    incelenebilir.
-   Demo verileriyle şablon önizlemesi yapılabilir.
-   Creator kendi gerçek bilgileriyle preview yapabilir.
-   Şablonların gelişimi mevcut yayınlanmış davetiyeleri bozmayacak
    şekilde ele alınır.

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
-   Harita
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

Fotoğraf/video dosyaları VPS diskinde veya PostgreSQL içinde tutulmaz.
Medya için **Cloudflare tarafındaki uygun storage/video altyapısı**
kullanılacaktır. Kesin servis seçimi teknik tasarım aşamasında güncel
seçeneklere göre belirlenir.

DB yalnızca medya metadata'sını, sahiplik bilgisini, key/URL bilgisini,
türünü, boyutunu ve durumunu tutar.

Fotoğraf/video adet ve boyut limitleri paketlere göre değişebilir.

## 10. RSVP

Creator isterse RSVP modülünü açabilir.

Varsayılan sorular sunulur ancak Creator: - değiştirebilir, -
silebilir, - yeni soru ekleyebilir, - sıralayabilir, - zorunlu/opsiyonel
yapabilir.

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
yönetebilir.

## 11. İletişim

Sistem gelin/damat gibi rollere sabitlenmez.

Creator birden fazla iletişim kişisi ekleyebilir. Her kayıt isim,
opsiyonel rol/açıklama, telefon ve uygun iletişim aksiyonlarını
içerebilir.

## 12. Anılarımız

Bu bölüm Creator'ın kendi galerisinden ayrıdır.

Davetliler: - mesaj, - emoji, - fotoğraf, - video

bırakabilir.

Görünürlüğü Creator seçer: - **Sadece Creator** - **Public**

Creator her durumda gönderileri yönetebilir veya gizleyebilir.

Misafir uploadlarında dosya türü/boyutu kontrolleri, güvenlik
kontrolleri ve rate limit uygulanır.

## 13. Hediye / Çeyiz Listesi

Altyapıda genel bir **Gift Registry** mantığı kullanılır. Görünen başlık
etkinlik türüne göre değişebilir.

Creator ürün ve istenen miktarı ekler. Davetliler ürünleri rezerve
edebilir.

Diğer davetliler rezervasyonu yapan kişinin kimliğini görmez; yalnızca
ürünün seçildiğini veya miktar ilerlemesini görür.

Creator kimin neyi seçtiğini görebilir ve gerektiğinde rezervasyonu
kaldırabilir.

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

`Draft → Active → Paused → Expired`

Creator davetiyesini yayınlayabilir, pasife alabilir ve tekrar
aktifleştirebilir.

Gerçekten gerekli ve şablonun düzgün çalışması için zorunlu bir alan
eksikse yayınlamaya izin verilmez.

Opsiyonel/recommended alanlar eksikse sistem uyarı verir ancak Creator
**Yine de Yayınla** diyebilir.

Validation şablonun ihtiyaçlarına göre çalışabilir.

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

Bireysel kullanıcılar için ağırlıklı olarak tek seferlik **yayın hakkı**
modeli düşünülür.

Başlangıç yaklaşımı: - **Free:** 1 davetiye, yaklaşık 1 günlük yayın -
**Standard:** 1 davetiye, maksimum 30 günlük yayın - **Premium:** Daha
uzun süre, daha yüksek limitler ve gelişmiş özellikler -
**Organization:** Abonelik tabanlı, çok sayıda davetiye yönetimine uygun

Kesin paket değerleri daha sonra DB üzerinden ayarlanabilir.

Creator paketinin izin verdiği maksimum süre içinde yayın tarihlerini
belirleyebilir.

Organizasyon paketleri ileride 10/50/unlimited gibi farklı limitlere
sahip olabilir.

## 20. DB-Driven Paket Sistemi

Paket özellikleri kod içine gömülmez.

Örnek özellikler:

-   `maxPublishDays`
-   `maxImages`
-   `maxVideos`
-   `maxImageSizeMb`
-   `maxVideoSizeMb`
-   `maxActiveInvitations`
-   `maxRSVPResponses`
-   `memoriesEnabled`
-   `giftRegistryEnabled`
-   `premiumTemplatesEnabled`

Paket limitleri mümkün olduğunca deploy gerektirmeden DB'den
değiştirilebilir.

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

## 23. Süresi Bitmiş veya Pasif Davetiye

Davetiyenin süresi bittiyse veya pasife alındıysa public içerik
gösterilmez.

Genel bir durum sayfası gösterilir:

> Bu davetiye şu anda yayında değil. Yayın süresi sona ermiş veya
> davetiye geçici olarak pasife alınmış olabilir.

İsim, fotoğraf, tarih gibi özel bilgiler gösterilmez.

Silinmiş veya hiç var olmamış URL normal 404 dönebilir.

## 24. Creator İstatistikleri

Creator kendi davetiyesi için bilgi/istatistik alanından genel verileri
görebilir:

-   Görüntülenme
-   Yaklaşık tekil ziyaretçi
-   RSVP cevapları
-   Toplam katılım sayısı
-   Anı sayısı
-   Medya sayısı
-   Hediye rezervasyonları

Amaç kişileri izlemek değil, davetiyenin genel durumunu göstermektir.

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

## 26. E-posta

Ürün ilk etapta bir **web application** olacaktır.

Native mobil uygulama, SMS, WhatsApp notification ve mobile push
notification MVP kapsamında değildir.

E-posta şu işlemlerde kullanılabilir: - E-posta doğrulama - Şifre
sıfırlama - Ödeme/paket işlemleri - Davetiye yayın işlemleri - Yayın
süresinin bitmesine yaklaşılması - Kritik hesap/güvenlik bildirimleri

Her RSVP/anı geldiğinde ayrı e-posta göndererek spam oluşturulmaz.

## 27. Dil ve Para Birimi

İlk sürüm:

-   **Türkçe**
-   **TRY**

Altyapı gelecekte çoklu dil ve para birimini destekleyebilecek şekilde
hazırlanır.

Frontend i18n uyumlu, backend/veri modeli de genişletilebilir olmalıdır.

## 28. Teknik Temel

-   **Frontend:** React
-   **Backend:** ASP.NET Core Web API / .NET
-   **Database:** PostgreSQL
-   **Version Control:** Git
-   **Sunucu:** Hostinger VPS
-   **Medya:** Cloudflare tarafında uygun storage/video servisleri
-   **Deployment:** Git push sonrasında otomatik build/deploy hedefi

Authentication ve authorization backend tarafından güvenli şekilde
uygulanır.

Tercihen Secure + HttpOnly + SameSite cookie tabanlı oturum yaklaşımı
kullanılır. Auth credential'ları plain localStorage içinde tutulmaz.

Public davetiye kodu hiçbir zaman Creator/Admin yetkisi sağlamaz.

## 29. Temel Ürün Prensibi

> **Bu bir tasarım editörü değil, hızlı davetiye oluşturma
> platformudur.**

Ana deneyim:

`Şablonunu seç → Bilgilerini gir → İstediğin özellikleri aç → Önizle → Yayınla`

Kullanıcı gereksiz seçeneklerle boğulmaz. Gelişmiş özellikler daha sonra
Creator Panel'den eklenebilir.

## 30. Backlog / MVP Dışı

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
**Agent geliştirme promptu değildir.** Teknik mimari, ödeme sağlayıcısı,
Cloudflare servis seçimi, e-posta sağlayıcısı, CI/CD, Docker/reverse
proxy, HTTPS, yedekleme ve operasyon detayları agent promptu
hazırlanırken ayrıca netleştirilecektir.
