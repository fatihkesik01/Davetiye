import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

const resources = {
  tr: {
    translation: {
      app: {
        eyebrow: 'Dijital davetiye platformu',
        title: 'Davetiye web temeli hazır',
        description:
          'Ürün ekranları, onaylanan kullanıcı akışlarıyla sonraki milestone’larda eklenecek.',
      },
      common: {
        loading: 'Yükleniyor…',
        skipToContent: 'Ana içeriğe geç',
      },
      error: {
        title: 'Bir sorun oluştu',
        description: 'Sayfayı yenileyip tekrar deneyin.',
      },
      notFound: {
        title: 'Sayfa bulunamadı',
        description: 'Aradığınız sayfa taşınmış veya kaldırılmış olabilir.',
      },
    },
  },
} as const

void i18n.use(initReactI18next).init({
  resources,
  lng: 'tr',
  fallbackLng: 'tr',
  supportedLngs: ['tr'],
  interpolation: {
    escapeValue: false,
  },
})

export { i18n }

