import { useEffect, useMemo, useRef, useState } from 'react'

import {
  DavetiyeApiClient,
  type PublicPlanCatalogItem,
  type TemplateCatalogItem,
} from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'
import { TemplateCatalogCard } from '../templates/catalog/TemplateCatalogPage'
import { describePlanLimits, formatPlanPrice } from './planFormatting'

// PRODUCT.md §30a is the only content source for this page; sections render in that order.
const SHOWCASE_TEMPLATE_COUNT = 3

type Remote<T> = { state: 'loading' } | { state: 'ready'; items: T[] } | { state: 'failed' }

const steps = [
  { title: 'Şablonunu seç', text: 'Etkinliğine uygun şablonu örnek içerikle incele ve seç.' },
  { title: 'Bilgilerini doldur', text: 'İsimleri, tarihi, mekânı ve istediğin bölümleri gir; bilgilerin tasarıma otomatik yerleşir.' },
  { title: 'Linkle paylaş', text: 'Önizle, yayınla ve tek linki davetlilerinle paylaş.' },
] as const

const features = [
  { title: 'RSVP ve katılım takibi', text: 'Davetliler katılıp katılmayacaklarını ve kaç kişi geleceklerini bildirir; yanıtları yalnızca sen görürsün.' },
  { title: 'Anılarımız', text: 'Davetliler sana mesaj ve emojiyle anı bırakır; herkese mi yalnızca sana mı görüneceğine sen karar verirsin.' },
  { title: 'Hediye / çeyiz listesi', text: 'İstediğin ürünleri ve adetleri ekle; davetliler rezerve eder, kimin neyi seçtiğini yalnızca sen görürsün.' },
  { title: 'QR ve paylaşım', text: 'Linki kopyala, WhatsApp’ta ya da cihazının paylaşım menüsüyle gönder, QR kodunu göster veya indir.' },
  { title: 'Takvim, geri sayım ve harita', text: 'Davetliler etkinliği takvimlerine ekler, kalan süreyi görür ve mekâna harita yönlendirmesiyle ulaşır.' },
  { title: 'Her ekranda uyumlu', text: 'Davetiyen telefonda, tablette ve bilgisayarda aynı linkle düzgün görünür.' },
  { title: 'Davetliler hesap açmaz', text: 'Davetlilerin yalnızca linke tıklaması yeterli; üyelik gerekmez.' },
] as const

const faqs = [
  {
    question: 'Davetlilerin hesap açması gerekiyor mu?',
    answer: 'Hayır. Davetliler kendilerine gönderilen linkle davetiyeyi açar. Senin açtığın bölümlere göre katılım yanıtı verebilir, anı bırakabilir veya hediye listesinden seçim yapabilirler. Hesap yalnızca davetiyeyi hazırlayan kişi için gerekir.',
  },
  {
    question: 'Yayın süresi nasıl işler?',
    answer: 'Her paketin bir yayın süresi vardır. Süre, davetiyen ilk kez yayına alındığında ya da planladığın başlangıç zamanında başlar ve takvime göre ilerler; davetiyeyi geçici olarak durdurmak süreyi uzatmaz. Süre dolduğunda davetiye yayından kalkar ama içeriğin korunur; yeni bir yayın hakkıyla aynı linkle yeniden yayına alabilirsin.',
  },
  {
    question: 'Ücretsiz paket neleri içerir?',
    answer: 'Ücretsiz paketle davetiyeni hazırlayabilir, önizleyebilir ve sınırlı bir süre için yayınlayabilirsin. Yayın süresi, katılım yanıtı sınırı ve hangi özelliklerin dahil olduğu, Paketler bölümündeki ücretsiz paket kartında güncel olarak yer alır.',
  },
  {
    question: 'Ödeme yaptıktan sonra davetiyem ne zaman yayına çıkar?',
    answer: 'Ödemeyi hesabını oluşturduktan sonra panelinden yaparsın. Yayın hakkı, ödemenin başarılı olduğu doğrulandıktan sonra açılır; ardından davetiyeni hemen yayınlayabilir veya yayın başlangıcını ileri bir tarihe planlayabilirsin. Yarım kalan ya da başarısız bir ödeme yayın hakkı açmaz; yeniden deneyebilirsin.',
  },
  {
    question: 'Yayındaki davetiyemi düzenleyebilir miyim?',
    answer: 'Evet. Değişikliklerini yapıp Güncelle dediğinde davetlilerin gördüğü sayfaya doğrudan yansır. Şablonu değiştirmek istersen önce davetiyeyi geçici olarak durdurman gerekir.',
  },
  {
    question: 'Davetiyemi nasıl paylaşırım?',
    answer: 'Panelinden davetiye linkini kopyalayabilir, WhatsApp’ta paylaşabilir, cihazının paylaşım menüsünü kullanabilir veya QR kodunu gösterip indirebilirsin.',
  },
] as const

function useRemoteList<T>(load: (signal: AbortSignal) => Promise<T[]>): Remote<T> {
  const [value, setValue] = useState<Remote<T>>({ state: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
      .then(items => setValue({ state: 'ready', items }))
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setValue({ state: 'failed' })
      })
    return () => controller.abort()
  }, [load])

  return value
}

export function LandingPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const loadTemplates = useMemo(() => (signal: AbortSignal) => api.listTemplates(signal), [api])
  const loadPlans = useMemo(() => (signal: AbortSignal) => api.listPublicPlans(signal), [api])
  const templates = useRemoteList<TemplateCatalogItem>(loadTemplates)
  const plans = useRemoteList<PublicPlanCatalogItem>(loadPlans)
  const headingReference = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    headingReference.current?.focus()
  }, [])

  return (
    <div className="landing">
      <a className="skip-link" href="#main-content">Ana içeriğe geç</a>
      <header className="shell-header landing-header">
        <p className="shell-brand landing-brand">Kutlio</p>
        <InternalLink className="landing-header__login" to="/giris">Giriş yap</InternalLink>
      </header>

      <main id="main-content" className="landing-main" tabIndex={-1}>
        <section className="landing-hero" aria-labelledby="landing-title">
          <div className="landing-container landing-hero__inner">
            <h1 id="landing-title" ref={headingReference} tabIndex={-1}>Davetiyeni dakikalar içinde hazırla</h1>
            <p className="landing-hero__lead">Şablonunu seç, bilgilerini gir, tek linkle tüm davetlilerine ulaştır.</p>
            <div className="landing-actions">
              <InternalLink className="button button--primary landing-button" to="/giris/kayit">Ücretsiz başla</InternalLink>
              <InternalLink className="button button--secondary landing-button" to="/sablonlar">Şablonları incele</InternalLink>
            </div>
          </div>
        </section>

        <section className="landing-section" aria-labelledby="landing-steps-heading">
          <div className="landing-container">
            <h2 id="landing-steps-heading">Nasıl çalışır?</h2>
            <ol className="landing-steps">
              {steps.map(step => <li key={step.title} className="landing-step">
                <h3>{step.title}</h3>
                <p>{step.text}</p>
              </li>)}
            </ol>
          </div>
        </section>

        <section className="landing-section landing-section--tinted" aria-labelledby="landing-templates-heading">
          <div className="landing-container">
            <div className="landing-section__heading">
              <h2 id="landing-templates-heading">Şablonlar</h2>
              <p>Her şablonu örnek içerikle inceleyebilir, beğendiğinle hemen başlayabilirsin.</p>
            </div>
            <TemplateShowcase templates={templates} />
            <p className="landing-section__more">
              <InternalLink className="button button--secondary landing-button" to="/sablonlar">Tüm şablonları gör</InternalLink>
            </p>
          </div>
        </section>

        <section className="landing-section" aria-labelledby="landing-features-heading">
          <div className="landing-container">
            <h2 id="landing-features-heading">Özellikler</h2>
            <ul className="landing-features">
              {features.map(feature => <li key={feature.title} className="landing-feature">
                <h3>{feature.title}</h3>
                <p>{feature.text}</p>
              </li>)}
            </ul>
          </div>
        </section>

        <section className="landing-section landing-section--tinted" aria-labelledby="landing-plans-heading">
          <div className="landing-container">
            <div className="landing-section__heading">
              <h2 id="landing-plans-heading">Paketler</h2>
              <p>Hesabını oluşturduktan sonra ihtiyacına uygun paketi panelinden seçebilirsin.</p>
            </div>
            <PlanCatalog plans={plans} />
          </div>
        </section>

        <section className="landing-section" aria-labelledby="landing-faq-heading">
          <div className="landing-container landing-container--narrow">
            <h2 id="landing-faq-heading">Sık sorulan sorular</h2>
            <div className="landing-faq">
              {faqs.map(faq => <details key={faq.question} className="landing-faq__item">
                <summary>{faq.question}</summary>
                <p>{faq.answer}</p>
              </details>)}
            </div>
          </div>
        </section>
      </main>

      <footer className="landing-footer">
        <div className="landing-container landing-footer__inner">
          <p className="landing-brand">Kutlio</p>
          <nav aria-label="Alt bilgi">
            <ul className="landing-footer__links">
              <li><InternalLink to="/gizlilik">Gizlilik</InternalLink></li>
              <li><InternalLink to="/kullanim-kosullari">Kullanım koşulları</InternalLink></li>
              <li><a href="mailto:destek@kutlio.com">destek@kutlio.com</a></li>
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  )
}

function TemplateShowcase({ templates }: { templates: Remote<TemplateCatalogItem> }) {
  if (templates.state === 'loading') return <LoadingState label="Şablonlar yükleniyor" />

  if (templates.state === 'failed' || templates.items.length === 0) {
    return <p className="landing-notice" role="status">
      {templates.state === 'failed'
        ? 'Şablonlar şu anda yüklenemedi. Tüm şablonlar sayfasından yeniden göz atabilirsin.'
        : 'Şu anda gösterilebilecek aktif şablon bulunmuyor.'}
    </p>
  }

  return <ul className="template-catalog__grid landing-templates">
    {templates.items.slice(0, SHOWCASE_TEMPLATE_COUNT).map(template => <li key={template.key}>
      <TemplateCatalogCard template={template} />
    </li>)}
  </ul>
}

function PlanCatalog({ plans }: { plans: Remote<PublicPlanCatalogItem> }) {
  if (plans.state === 'loading') return <LoadingState label="Paketler yükleniyor" />

  if (plans.state === 'failed' || plans.items.length === 0) {
    return <div className="landing-notice" role="status">
      <p>Paket bilgileri şu anda gösterilemiyor. Hesabını oluşturarak hemen başlayabilir, paketleri daha sonra panelinden inceleyebilirsin.</p>
      <InternalLink className="button button--primary landing-button" to="/giris/kayit">Ücretsiz başla</InternalLink>
    </div>
  }

  return <ul className="landing-plans">
    {plans.items.map(plan => <li key={plan.key}>
      <article className="landing-plan" aria-labelledby={`landing-plan-${plan.key}`}>
        <h3 id={`landing-plan-${plan.key}`}>{plan.displayName}</h3>
        <p className="landing-plan__price">{formatPlanPrice(plan)}</p>
        {plan.description ? <p className="landing-plan__description">{plan.description}</p> : null}
        <dl className="landing-plan__limits">
          {describePlanLimits(plan).map(row => <div key={row.label}>
            <dt>{row.label}</dt>
            <dd>{row.value}</dd>
          </div>)}
        </dl>
        <InternalLink className="button button--secondary landing-button landing-plan__action" to="/giris/kayit">
          {plan.displayName} ile başla
        </InternalLink>
      </article>
    </li>)}
  </ul>
}
