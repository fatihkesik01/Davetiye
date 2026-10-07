import type { DraftContentInput } from '../../../api/generated/client'
import { normalizeDraftContent, type NormalizedInvitationRenderModel } from './model'

const demos: Record<string, DraftContentInput> = {
  'zamansiz-dugun': {
    eventType: 'dugun', headline: 'Zeynep & Kerem', hostNames: ['Zeynep', 'Kerem'],
    message: 'Bu güzel başlangıcımızda sizleri de aramızda görmekten mutluluk duyarız.',
    startsAt: '2027-06-19T16:00:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Sait Halim Paşa Yalısı', address: 'Yeniköy, İstanbul' },
    programItems: [{ title: 'Karşılama', startsAt: '2027-06-19T16:00:00+03:00' }, { title: 'Nikâh', startsAt: '2027-06-19T17:00:00+03:00' }],
  },
  'romantik-nisan': {
    eventType: 'nisan', headline: 'Elif ile Can nişanlanıyor', hostNames: ['Elif', 'Can'],
    message: 'Yeni hikâyemizin ilk sayfasını birlikte açalım.',
    startsAt: '2027-04-24T19:30:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'La Vie Bahçe', address: 'Çankaya, Ankara' }, programItems: [],
  },
  'gece-kina': {
    eventType: 'kina', headline: 'Selin’in Kına Gecesi', hostNames: ['Selin'],
    message: 'Işıklar, kahkahalar ve unutulmaz bir gece için buluşuyoruz.',
    startsAt: '2027-05-08T20:00:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Atlas Davet', address: 'Nilüfer, Bursa' },
    programItems: [{ title: 'Kapı Açılışı', startsAt: '2027-05-08T19:30:00+03:00' }, { title: 'Kına Seremonisi', startsAt: '2027-05-08T21:00:00+03:00' }],
  },
  'neseli-sunnet': {
    eventType: 'sunnet', headline: 'Aras’ın Sünnet Şöleni', hostNames: ['Aras'],
    message: 'Oyun, müzik ve neşeyle dolu bu özel günde sizi de bekliyoruz.',
    startsAt: '2027-07-11T14:00:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Mavi Köşk', address: 'Konak, İzmir' }, programItems: [],
  },
  'renkli-dogum-gunu': {
    eventType: 'dogum-gunu', headline: 'Mina 7 yaşında!', hostNames: ['Mina'],
    message: 'Pastalar hazır, balonlar havada. Eğlenceye katıl!',
    startsAt: '2027-03-13T13:30:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Renkli Bahçe', address: 'Kadıköy, İstanbul' }, programItems: [],
  },
  'pastel-baby-shower': {
    eventType: 'baby-shower', headline: 'Minik bir merhaba', hostNames: ['Duru', 'Mert'],
    message: 'Bebeğimizi beklerken sevincimizi sizinle paylaşmak istiyoruz.',
    startsAt: '2027-02-21T15:00:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Limon Çiçeği', address: 'Muratpaşa, Antalya' }, programItems: [],
  },
  'modern-mezuniyet': {
    eventType: 'mezuniyet', headline: 'Sınıf ’27', hostNames: ['Deniz Aksoy'],
    message: 'Bir dönemi kapatıyor, yenisine cesaretle başlıyoruz.',
    startsAt: '2027-06-27T18:00:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Kampüs Açık Hava Sahnesi', address: 'Eskişehir' },
    programItems: [{ title: 'Tören', startsAt: '2027-06-27T18:00:00+03:00' }, { title: 'Kutlama', startsAt: '2027-06-27T20:00:00+03:00' }],
  },
  'minimal-acilis': {
    eventType: 'acilis-genel', headline: 'Studio No. 8 açılıyor', hostNames: ['Studio No. 8'],
    message: 'Yeni mekânımızı birlikte keşfetmek için sizi açılışa davet ediyoruz.',
    startsAt: '2027-01-16T18:30:00+03:00', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Studio No. 8', address: 'Karaköy, İstanbul' }, programItems: [],
  },
}

export function getTemplateDemoModel(templateKey: string): NormalizedInvitationRenderModel | null {
  const content = demos[templateKey]
  return content ? normalizeDraftContent(content) : null
}
