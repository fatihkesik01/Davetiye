import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import {
  DavetiyeApiClient,
  type PublicPlanCatalogItem,
  type TemplateCatalogItem,
} from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { SiteHeader } from '../../components/layout/SiteHeader'
import { InternalLink } from '../../components/ui/InternalLink'
import { TemplateCatalogCard } from '../templates/catalog/TemplateCatalogPage'
import { describePlanLimits, formatPlanPrice } from './planFormatting'
import { landingUiTranslations } from '../../i18n/landingUi'

// PRODUCT.md §30a is the only content source for this page; sections render in that order.
const SHOWCASE_TEMPLATE_COUNT = 3

type Remote<T> = { state: 'loading' } | { state: 'ready'; items: T[] } | { state: 'failed' }

const featureIcons = ['rsvp', 'memory', 'gift', 'share', 'calendar', 'devices', 'guest'] as const

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
  const { t, i18n } = useTranslation()
  const locale = i18n.language === 'en' ? 'en' : 'tr'
  const copy = t('landingUi', { returnObjects: true }) as unknown as typeof landingUiTranslations.tr
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const loadTemplates = useMemo(() => (signal: AbortSignal) => api.listTemplates(signal), [api])
  const loadPlans = useMemo(() => (signal: AbortSignal) => api.listPublicPlans(signal), [api])
  const templates = useRemoteList<TemplateCatalogItem>(loadTemplates)
  const plans = useRemoteList<PublicPlanCatalogItem>(loadPlans)
  const headingReference = useRef<HTMLHeadingElement>(null)
  const landingReference = useRef<HTMLDivElement>(null)

  useEffect(() => {
    headingReference.current?.focus()
  }, [])

  useEffect(() => {
    const landing = landingReference.current
    if (!landing || typeof IntersectionObserver === 'undefined'
      || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return

    const motionItems = landing.querySelectorAll<HTMLElement>('[data-landing-motion]')
    if (motionItems.length === 0) return

    let frame = 0
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue
        entry.target.classList.add('landing-motion-entered')
        observer.unobserve(entry.target)
      }
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 })

    for (const item of motionItems) item.classList.add('landing-motion-pending')
    frame = window.requestAnimationFrame(() => {
      for (const item of motionItems) {
        item.classList.add('landing-motion-ready')
        observer.observe(item)
      }
    })

    return () => {
      window.cancelAnimationFrame(frame)
      observer.disconnect()
      for (const item of motionItems) {
        item.classList.remove('landing-motion-pending', 'landing-motion-ready', 'landing-motion-entered')
      }
    }
  }, [])

  return (
    <div className="landing" ref={landingReference}>
      <a className="skip-link" href="#main-content">{copy.skip}</a>
      <SiteHeader zone="public" sectionLabel={copy.navLabel} sectionLinks={[
        { href: '#how-it-works', label: copy.navHow }, { href: '#features', label: copy.navFeatures },
        { href: '#plans', label: copy.navPlans }, { href: '#faq', label: copy.navFaq },
      ]} />

      <main id="main-content" className="landing-main" tabIndex={-1}>
        <section className="landing-hero" aria-labelledby="landing-title">
          <div className="landing-container landing-hero__inner">
            <div className="landing-hero__copy">
              <h1 id="landing-title" ref={headingReference} tabIndex={-1}>{copy.heroTitle}</h1>
              <p className="landing-hero__lead">{copy.heroLead}</p>
              <div className="landing-actions">
                <InternalLink className="button button--primary landing-button" to="/giris/kayit">{copy.startFree}</InternalLink>
                <InternalLink className="button button--secondary landing-button" to="/sablonlar">{copy.seeTemplates}</InternalLink>
              </div>
            </div>
            <HeroArtwork />
          </div>
        </section>

        <section id="how-it-works" className="landing-section" aria-labelledby="landing-steps-heading">
          <div className="landing-container">
            <h2 id="landing-steps-heading">{copy.how}</h2>
            <ol className="landing-steps">
              {copy.steps.map(step => <li key={step.title} className="landing-step" data-landing-motion>
                <div className="landing-step__content">
                  <h3>{step.title}</h3>
                  <p>{step.text}</p>
                </div>
              </li>)}
            </ol>
          </div>
        </section>

        <section className="landing-section landing-section--tinted" aria-labelledby="landing-templates-heading">
          <div className="landing-container">
            <div className="landing-section__heading">
              <h2 id="landing-templates-heading">{copy.templatesTitle}</h2>
              <p>{copy.templatesLead}</p>
            </div>
            <TemplateShowcase templates={templates} copy={copy} />
            <p className="landing-section__more">
              <InternalLink className="button button--secondary landing-button" to="/sablonlar">{copy.allTemplates}</InternalLink>
            </p>
          </div>
        </section>

        <section id="features" className="landing-section" aria-labelledby="landing-features-heading">
          <div className="landing-container">
            <h2 id="landing-features-heading">{copy.featuresTitle}</h2>
            <ul className="landing-features">
              {copy.features.map((feature, index) => <li key={feature.title} className="landing-feature" data-landing-motion>
                <span className="landing-feature__icon"><FeatureIcon name={featureIcons[index] ?? 'guest'} /></span>
                <div className="landing-feature__copy">
                  <h3>{feature.title}</h3>
                  <p>{feature.text}</p>
                </div>
              </li>)}
            </ul>
          </div>
        </section>

        <section id="plans" className="landing-section landing-section--tinted" aria-labelledby="landing-plans-heading">
          <div className="landing-container">
            <div className="landing-section__heading">
              <h2 id="landing-plans-heading">{copy.plansTitle}</h2>
              <p>{copy.plansLead}</p>
            </div>
            <PlanCatalog plans={plans} copy={copy} locale={locale} />
          </div>
        </section>

        <section id="faq" className="landing-section" aria-labelledby="landing-faq-heading">
          <div className="landing-container landing-container--narrow">
            <h2 id="landing-faq-heading">{copy.faqTitle}</h2>
            <div className="landing-faq">
              {copy.faq.map(faq => <details key={faq.question} className="landing-faq__item">
                <summary>{faq.question}</summary>
                <p>{faq.answer}</p>
              </details>)}
            </div>
          </div>
        </section>
      </main>

      <footer className="landing-footer">
        <div className="landing-container landing-footer__inner">
          <p className="landing-brand">{t('siteHeader.brand')}</p>
          <nav aria-label={copy.footerLabel}>
            <ul className="landing-footer__links">
              <li><InternalLink to="/gizlilik">{copy.privacy}</InternalLink></li>
              <li><InternalLink to="/kullanim-kosullari">{copy.terms}</InternalLink></li>
              <li><a href={`mailto:${copy.support}`}>{copy.support}</a></li>
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  )
}

function TemplateShowcase({ templates, copy }: { templates: Remote<TemplateCatalogItem>; copy: typeof landingUiTranslations.tr }) {
  if (templates.state === 'loading') return <LoadingState label={copy.templatesLoading} />

  if (templates.state === 'failed' || templates.items.length === 0) {
    return <p className="landing-notice" role="status">
      {templates.state === 'failed'
        ? copy.templatesFailed
        : copy.templatesEmpty}
    </p>
  }

  return <ul className="template-catalog__grid landing-templates">
    {templates.items.slice(0, SHOWCASE_TEMPLATE_COUNT).map(template => <li key={template.key}>
      <TemplateCatalogCard template={template} />
    </li>)}
  </ul>
}

// Decorative artwork colors follow the active palette so it stays coherent in every theme and in dark mode.
const ACCENT = 'var(--account-accent)'
const PAPER = 'color-mix(in srgb, var(--account-surface) 94%, var(--account-accent))'
const EDGE = 'color-mix(in srgb, var(--account-accent) 24%, var(--account-surface))'
const mix = (percent: number) => `color-mix(in srgb, var(--account-accent) ${percent}%, var(--account-surface))`
const lighten = (percent: number) => `color-mix(in srgb, var(--account-accent) ${100 - percent}%, #fff)`

function HeroArtwork() {
  return (
    <div className="landing-artwork" aria-hidden="true">
      <svg className="landing-artwork__svg" viewBox="0 0 520 440" fill="none" xmlns="http://www.w3.org/2000/svg">
        <ellipse cx="270" cy="373" rx="183" ry="27" style={{ fill: ACCENT }} fillOpacity=".1" />
        <circle cx="365" cy="115" r="79" style={{ fill: mix(24) }} />
        <circle cx="365" cy="115" r="58" style={{ stroke: ACCENT }} strokeOpacity=".26" />
        <path d="M78 118C113 82 139 71 177 65" style={{ stroke: ACCENT }} strokeOpacity=".35" strokeWidth="2" strokeLinecap="round" strokeDasharray="2 9" />
        <path d="M406 251C439 269 455 291 461 320" style={{ stroke: ACCENT }} strokeOpacity=".35" strokeWidth="2" strokeLinecap="round" strokeDasharray="2 9" />
        <g transform="rotate(-7 251 205)">
          <rect x="134" y="66" width="230" height="278" rx="17" style={{ fill: PAPER, stroke: EDGE }} strokeWidth="2" />
          <rect x="154" y="87" width="190" height="236" rx="10" style={{ fill: mix(8) }} />
          <circle cx="249" cy="205" r="28" style={{ fill: mix(30) }} />
        </g>
        <g transform="rotate(5 275 289)">
          <rect x="103" y="246" width="292" height="145" rx="18" style={{ fill: ACCENT }} />
          <path d="M108 265L249 359C255 363 263 363 269 359L390 277" style={{ fill: lighten(18) }} />
          <path className="landing-artwork__flap" d="M108 263L240 342C250 348 263 348 273 342L390 263" style={{ fill: lighten(30), stroke: lighten(45) }} strokeWidth="2" />
          <path d="M109 377L207 307M389 377L291 307" style={{ stroke: lighten(45) }} strokeOpacity=".65" strokeWidth="2" />
          <circle cx="250" cy="320" r="16" style={{ fill: PAPER }} />
          <path d="M244 320L248 324L256 315" style={{ stroke: ACCENT }} strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
        </g>
        <g transform="rotate(12 413 333)">
          <circle cx="420" cy="333" r="35" style={{ fill: PAPER, stroke: EDGE }} strokeWidth="2" />
          <path d="M406 339l11-12a7 7 0 0 1 10 10l-7 7m-2-15 7-7a7 7 0 0 1 10 10l-11 12a7 7 0 0 1-10 0" style={{ stroke: ACCENT }} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
        </g>
        <g transform="rotate(-12 99 311)">
          <circle cx="99" cy="318" r="34" style={{ fill: mix(14) }} />
          <path d="m84 318 10 10 20-22" style={{ stroke: ACCENT }} strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
        </g>
      </svg>
    </div>
  )
}

function FeatureIcon({ name }: { name: typeof featureIcons[number] }) {
  const paths = {
    rsvp: <><path d="M5 6.5A2.5 2.5 0 0 1 7.5 4h9A2.5 2.5 0 0 1 19 6.5v6a2.5 2.5 0 0 1-2.5 2.5H11l-4 3v-3.2A2.5 2.5 0 0 1 5 12.5v-6Z" /><path d="m9 9.5 2 2 4-4" /></>,
    memory: <><path d="M12 20s-7-4.4-7-9.2A3.8 3.8 0 0 1 12 8.5a3.8 3.8 0 0 1 7 2.3C19 15.6 12 20 12 20Z" /><path d="M4 5V3m16 2V3M3 12H1m22 0h-2" /></>,
    gift: <><path d="M4 10h16v10H4zM3 7h18v3H3zM12 7v13" /><path d="M12 7H8.5a2.5 2.5 0 1 1 2.3-3.5L12 7Zm0 0h3.5a2.5 2.5 0 1 0-2.3-3.5L12 7Z" /></>,
    share: <><rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><path d="M15 14h2v2h3v4h-5v-3h-2v-2h2v-1Z" /></>,
    calendar: <><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4m8-4v4M4 10h16m-11 4h2v2H9z" /></>,
    devices: <><rect x="3" y="5" width="13" height="10" rx="1.5" /><path d="M7 19h5m-2-4v4" /><rect x="17" y="8" width="4" height="10" rx="1" /></>,
    guest: <><circle cx="12" cy="8" r="3" /><path d="M5 20a7 7 0 0 1 14 0M18 5a3 3 0 0 1 0 6m1 3a5 5 0 0 1 4 5" /></>,
  }

  return <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">{paths[name]}</svg>
}

function PlanCatalog({ plans, copy, locale }: { plans: Remote<PublicPlanCatalogItem>; copy: typeof landingUiTranslations.tr; locale: 'tr' | 'en' }) {
  if (plans.state === 'loading') return <LoadingState label={copy.plansLoading} />

  if (plans.state === 'failed' || plans.items.length === 0) {
    return <div className="landing-notice landing-notice--plans" role="status">
      <p>{copy.plansUnavailable}</p>
      <InternalLink className="button button--primary landing-button" to="/giris/kayit">{copy.startFree}</InternalLink>
    </div>
  }

  return <ul className="landing-plans">
    {plans.items.map(plan => <li key={plan.key}>
      <article className="landing-plan" aria-labelledby={`landing-plan-${plan.key}`}>
        <h3 id={`landing-plan-${plan.key}`}>{plan.displayName}</h3>
        <p className="landing-plan__price">{formatPlanPrice(plan, locale)}</p>
        {plan.description ? <p className="landing-plan__description">{plan.description}</p> : null}
        <dl className="landing-plan__limits">
          {describePlanLimits(plan, locale).map(row => <div key={row.label}>
            <dt>{row.label}</dt>
            <dd>{row.value}</dd>
          </div>)}
        </dl>
        <InternalLink className="button button--secondary landing-button landing-plan__action" to="/giris/kayit">
          {locale === 'tr' ? `${plan.displayName} ile başla` : `Choose ${plan.displayName}`}
        </InternalLink>
      </article>
    </li>)}
  </ul>
}
