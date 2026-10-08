import { fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { PublicPlanCatalogItem, TemplateCatalogItem } from '../../api/generated/client'
import { App } from '../../App'
import { LandingPage } from './LandingPage'
import { formatPlanPrice } from './planFormatting'

const template = (key: string, name: string): TemplateCatalogItem => ({
  key,
  name,
  description: `${name} açıklaması`,
  category: 'Düğün',
  isPremium: false,
  rendererVersion: 1,
  previewImageUrl: `/template-previews/${key}.jpg`,
  supportedModules: ['hero'],
  requiredFields: ['headline'],
  recommendedFields: [],
})

const plan = (overrides: Partial<PublicPlanCatalogItem>): PublicPlanCatalogItem => ({
  key: 'free',
  displayName: 'Free',
  description: null,
  priceAmount: 0,
  currency: 'TRY',
  billingPeriod: 'free',
  maxPublishDays: 1,
  maxActiveInvitations: 1,
  maxImages: 5,
  maxVideos: 0,
  maxRSVPResponses: 50,
  memoriesEnabled: false,
  giftRegistryEnabled: false,
  premiumTemplatesEnabled: false,
  ...overrides,
})

const plans = [
  plan({}),
  plan({ key: 'premium', displayName: 'Premium', description: 'Geniş kapsamlı paket.', priceAmount: 1199, billingPeriod: 'one-time', maxPublishDays: 90, maxRSVPResponses: 1000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true }),
  plan({ key: 'organization', displayName: 'Organization', priceAmount: 2499, billingPeriod: 'monthly', maxPublishDays: 365, maxActiveInvitations: 10, maxRSVPResponses: 5000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true }),
]

function stubApi({ templates, plansResponse }: { templates: Response | Error; plansResponse: Response | Error }) {
  const fetch = vi.fn((input: RequestInfo | URL) => {
    const url = String(input)
    const answer = url === '/api/v1/templates' ? templates : url === '/api/v1/public/plans' ? plansResponse : new Error(`Unexpected request: ${url}`)
    return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer.clone())
  })
  vi.stubGlobal('fetch', fetch)
  return fetch
}

const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } })

describe('LandingPage', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('renders the PRODUCT.md §30a sections in order with a single h1 and the hero actions', async () => {
    stubApi({ templates: json([]), plansResponse: json([]) })
    render(<LandingPage />)

    expect(screen.getAllByRole('heading', { level: 1 }).map(heading => heading.textContent)).toEqual(['Davetiyeni dakikalar içinde hazırla'])
    expect(screen.getByRole('heading', { level: 1 })).toBe(document.activeElement)
    expect(screen.getByText('Şablonunu seç, bilgilerini gir, tek linkle tüm davetlilerine ulaştır.')).toBeTruthy()
    expect(screen.getAllByRole('heading', { level: 2 }).map(heading => heading.textContent)).toEqual([
      'Nasıl çalışır?', 'Şablonlar', 'Özellikler', 'Paketler', 'Sık sorulan sorular',
    ])
    const hero = screen.getByRole('region', { name: 'Davetiyeni dakikalar içinde hazırla' })
    expect(within(hero).getByRole('link', { name: 'Ücretsiz başla' }).getAttribute('href')).toBe('/giris/kayit')
    expect(within(hero).getByRole('link', { name: 'Şablonları incele' }).getAttribute('href')).toBe('/sablonlar')
    expect(screen.getByText('Ana içeriğe geç').getAttribute('href')).toBe('#main-content')
    expect(screen.getByRole('main').id).toBe('main-content')
    expect(screen.queryByText(/fotoğraf|video/i)).toBeNull()

    const footer = screen.getByRole('contentinfo')
    expect(within(footer).getByRole('link', { name: 'Gizlilik' }).getAttribute('href')).toBe('/gizlilik')
    expect(within(footer).getByRole('link', { name: 'Kullanım koşulları' }).getAttribute('href')).toBe('/kullanim-kosullari')
    expect(within(footer).getByRole('link', { name: 'destek@kutlio.com' }).getAttribute('href')).toBe('mailto:destek@kutlio.com')
    await screen.findByText('Şu anda gösterilebilecek aktif şablon bulunmuyor.')
  })

  it('shows a few live templates with demo links and a link to the full catalog', async () => {
    stubApi({
      templates: json([template('zamansiz-dugun', 'Zamansız Düğün'), template('romantik-nisan', 'Romantik Nişan'), template('gece-kina', 'Gece Kına'), template('minimal-acilis', 'Minimal Açılış')]),
      plansResponse: json(plans),
    })
    render(<LandingPage />)

    const section = screen.getByRole('region', { name: 'Şablonlar' })
    expect(await within(section).findByRole('heading', { name: 'Zamansız Düğün' })).toBeTruthy()
    expect(within(section).getAllByRole('heading', { level: 3 })).toHaveLength(3)
    expect(within(section).queryByRole('heading', { name: 'Minimal Açılış' })).toBeNull()
    expect(within(section).getAllByRole('link', { name: 'Demoyu incele' })[0]!.getAttribute('href')).toBe('/sablonlar/zamansiz-dugun')
    expect(within(section).getByRole('link', { name: 'Tüm şablonları gör' }).getAttribute('href')).toBe('/sablonlar')
  })

  it('keeps the page usable when the template catalog fails', async () => {
    stubApi({ templates: new Error('offline'), plansResponse: json(plans) })
    render(<LandingPage />)

    const section = screen.getByRole('region', { name: 'Şablonlar' })
    expect(await within(section).findByText(/Şablonlar şu anda yüklenemedi/)).toBeTruthy()
    expect(within(section).getByRole('link', { name: 'Tüm şablonları gör' }).getAttribute('href')).toBe('/sablonlar')
    expect(await screen.findByRole('heading', { name: 'Premium' })).toBeTruthy()
  })

  it('renders live plans in API order with formatted prices, limits and registration links', async () => {
    stubApi({ templates: json([]), plansResponse: json(plans) })
    render(<LandingPage />)

    const section = screen.getByRole('region', { name: 'Paketler' })
    await within(section).findByRole('heading', { name: 'Free' })
    const cards = within(section).getAllByRole('article')
    expect(cards.map(card => within(card).getByRole('heading').textContent)).toEqual(['Free', 'Premium', 'Organization'])

    expect(within(cards[0]!).getByText('Ücretsiz')).toBeTruthy()
    expect(within(cards[1]!).getByText('1.199 TRY / tek sefer')).toBeTruthy()
    expect(within(cards[2]!).getByText('2.499 TRY / ay')).toBeTruthy()
    expect(within(cards[1]!).getByText('Geniş kapsamlı paket.')).toBeTruthy()

    const premiumLimits = cards[1]!.querySelector('dl')!
    expect(Array.from(premiumLimits.querySelectorAll('div')).map(row => `${row.querySelector('dt')!.textContent}: ${row.querySelector('dd')!.textContent}`)).toEqual([
      'Yayın süresi: 90 gün',
      'Aynı anda aktif davetiye: 1',
      'Davetiye başına katılım yanıtı (RSVP): 1.000',
      'Anılarımız: Dahil',
      'Hediye / çeyiz listesi: Dahil',
      'Premium şablonlar: Dahil',
    ])
    expect(within(cards[0]!).getAllByText('Dahil değil')).toHaveLength(3)
    expect(within(cards[2]!).getByText('5.000')).toBeTruthy()
    expect(within(section).queryByText(/fotoğraf|video/i)).toBeNull()
    for (const card of cards) {
      expect(within(card).getByRole('link').getAttribute('href')).toBe('/giris/kayit')
    }
    expect(within(section).getByRole('link', { name: 'Organization ile başla' })).toBeTruthy()
  })

  it.each([
    ['the request fails', new Error('offline')],
    ['no plan is active', json([])],
  ])('shows a calm registration fallback when %s', async (_, plansResponse) => {
    stubApi({ templates: json([]), plansResponse })
    render(<LandingPage />)

    const section = screen.getByRole('region', { name: 'Paketler' })
    expect(await within(section).findByText(/Paket bilgileri şu anda gösterilemiyor/)).toBeTruthy()
    expect(within(section).getByRole('link', { name: 'Ücretsiz başla' }).getAttribute('href')).toBe('/giris/kayit')
    expect(within(section).queryByRole('alert')).toBeNull()
  })

  it('discloses FAQ answers on demand', async () => {
    stubApi({ templates: json([]), plansResponse: json([]) })
    render(<LandingPage />)

    const section = screen.getByRole('region', { name: 'Sık sorulan sorular' })
    const items = section.querySelectorAll('details')
    expect(items.length).toBeGreaterThanOrEqual(4)
    expect(items.length).toBeLessThanOrEqual(6)
    const first = items[0]!
    expect(first.open).toBe(false)
    fireEvent.click(within(section).getByText('Davetlilerin hesap açması gerekiyor mu?'))
    expect(first.open).toBe(true)
    await screen.findByText(/Paket bilgileri şu anda gösterilemiyor/)
  })
})

describe('formatPlanPrice', () => {
  it.each([
    [{ priceAmount: 0, currency: 'TRY', billingPeriod: 'free' as const }, 'Ücretsiz'],
    [{ priceAmount: 699, currency: 'TRY', billingPeriod: 'one-time' as const }, '699 TRY / tek sefer'],
    [{ priceAmount: 1199, currency: 'TRY', billingPeriod: 'one-time' as const }, '1.199 TRY / tek sefer'],
    [{ priceAmount: 2499.5, currency: 'TRY', billingPeriod: 'monthly' as const }, '2.499,5 TRY / ay'],
  ])('formats %o as %s', (input, expected) => {
    expect(formatPlanPrice(input)).toBe(expected)
  })
})

describe('landing route', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('serves the landing page at / instead of the not-found page', async () => {
    stubApi({ templates: json([]), plansResponse: json([]) })
    window.history.replaceState({}, '', '/')
    render(<App />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Davetiyeni dakikalar içinde hazırla' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Sayfa bulunamadı' })).toBeNull()
    expect(document.title).toBe('Dijital davetiye | Kutlio')
    await screen.findByText(/Paket bilgileri şu anda gösterilemiyor/)
  })
})
