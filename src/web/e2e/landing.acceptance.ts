import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const templates = ['zamansiz-dugun', 'romantik-nisan', 'gece-kina', 'neseli-sunnet'].map((key, index) => ({
  key,
  name: ['Zamansız Düğün', 'Romantik Nişan', 'Gece Kına', 'Neşeli Sünnet'][index],
  description: 'Örnek içerikle incelenebilen şablon.',
  category: ['Düğün', 'Nişan', 'Kına', 'Sünnet'][index],
  isPremium: index === 1,
  rendererVersion: 1,
  previewImageUrl: `/template-previews/${key}.jpg`,
  supportedModules: ['hero', 'venue'],
  requiredFields: ['headline'],
  recommendedFields: [],
}))

const basePlan = { description: null, currency: 'TRY', maxImages: 5, maxVideos: 0, maxActiveInvitations: 1 }
const plans = [
  { ...basePlan, key: 'free', displayName: 'Free', priceAmount: 0, billingPeriod: 'free', maxPublishDays: 1, maxRSVPResponses: 50, memoriesEnabled: false, giftRegistryEnabled: false, premiumTemplatesEnabled: false },
  { ...basePlan, key: 'standard', displayName: 'Standard', priceAmount: 699, billingPeriod: 'one-time', maxPublishDays: 30, maxRSVPResponses: 300, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: false },
  { ...basePlan, key: 'premium', displayName: 'Premium', priceAmount: 1199, billingPeriod: 'one-time', maxPublishDays: 90, maxRSVPResponses: 1000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true },
  { ...basePlan, key: 'organization', displayName: 'Organization', priceAmount: 2499, billingPeriod: 'monthly', maxPublishDays: 365, maxActiveInvitations: 10, maxRSVPResponses: 5000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true },
]

async function mockApi(page: Page, { failing = false } = {}) {
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (failing) return route.fulfill({ status: 503, contentType: 'application/problem+json', json: { status: 503 } })
    if (path === '/api/v1/templates') return route.fulfill({ contentType: 'application/json', json: templates })
    if (path === '/api/v1/public/plans') return route.fulfill({ contentType: 'application/json', json: plans })
    return route.fulfill({ status: 404, contentType: 'application/problem+json', json: { status: 404 } })
  })
}

async function applyZoom(page: Page, projectName: string) {
  if (projectName === 'chromium-200pct') {
    await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  }
}

async function expectResponsiveAndAccessible(page: Page) {
  const viewportMetrics = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))
  expect(viewportMetrics.scrollWidth).toBeLessThanOrEqual(viewportMetrics.clientWidth)

  const accessibility = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze()
  expect(accessibility.violations).toEqual([])
}

test('landing page renders live templates and plans responsively without WCAG A/AA violations or third-party requests', async ({ page }, testInfo) => {
  const externalRequests: string[] = []
  page.on('request', request => {
    const url = new URL(request.url())
    if (url.origin !== 'http://127.0.0.1:4173') externalRequests.push(url.origin)
  })
  await mockApi(page)
  await page.goto('/')
  await applyZoom(page, testInfo.project.name)

  await expect(page).toHaveTitle('Dijital davetiye | Kutlio')
  await expect(page.locator('meta[name="description"]')).toHaveAttribute('content', /Kutlio/)
  await expect(page.locator('meta[name="robots"]')).toHaveCount(0)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Davetiyeni dakikalar içinde hazırla')
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
  await expect(page.getByRole('heading', { level: 3, name: 'Romantik Nişan' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 3, name: 'Neşeli Sünnet' })).toHaveCount(0)
  await expect(page.getByText('1.199 TRY / tek sefer')).toBeVisible()
  await expect(page.getByText('2.499 TRY / ay')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Premium ile başla' })).toHaveAttribute('href', '/giris/kayit')
  await expect(page.getByText(/fotoğraf|video/i)).toHaveCount(0)
  await expect(page.locator('img[src^="/template-previews/"]').first()).toBeVisible()

  await expectResponsiveAndAccessible(page)
  await page.locator('.landing-faq__item').first().evaluate(element => { (element as HTMLDetailsElement).open = true })
  await expectResponsiveAndAccessible(page)
  expect(externalRequests).toEqual([])
  await page.screenshot({ path: testInfo.outputPath('landing.png'), fullPage: true })
})

test('landing page stays usable with calm fallbacks when the catalog and plan requests fail', async ({ page }, testInfo) => {
  await mockApi(page, { failing: true })
  await page.goto('/')
  await applyZoom(page, testInfo.project.name)

  await expect(page.getByText(/Şablonlar şu anda yüklenemedi/)).toBeVisible()
  await expect(page.getByText(/Paket bilgileri şu anda gösterilemiyor/)).toBeVisible()
  await expect(page.getByRole('region', { name: 'Paketler' }).getByRole('link', { name: 'Ücretsiz başla' })).toHaveAttribute('href', '/giris/kayit')
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expectResponsiveAndAccessible(page)
  await page.screenshot({ path: testInfo.outputPath('landing-fallback.png'), fullPage: true })
})

test('landing page is keyboard reachable in logical order and FAQ disclosures toggle from the keyboard', async ({ page }) => {
  await mockApi(page)
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1 })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Ücretsiz başla' }).first()).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Şablonları incele' })).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await page.keyboard.press('Shift+Tab')
  await expect(page.getByRole('link', { name: 'Giriş yap' })).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await expect(page.getByRole('link', { name: 'Ana içeriğe geç' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.locator('#main-content')).toBeFocused()

  await expect(page.getByRole('link', { name: 'Organization ile başla' })).toBeVisible()
  const firstQuestion = page.locator('.landing-faq__item summary').first()
  await firstQuestion.focus()
  await page.keyboard.press('Enter')
  await expect(page.locator('.landing-faq__item').first()).toHaveAttribute('open', '')
  await expect(page.getByText(/Hesap yalnızca davetiyeyi hazırlayan kişi için gerekir/)).toBeVisible()

  const reached: string[] = []
  for (let index = 0; index < 20 && !reached.includes('mailto:destek@kutlio.com'); index += 1) {
    await page.keyboard.press('Tab')
    const href = await page.evaluate(() => document.activeElement?.getAttribute('href') ?? document.activeElement?.tagName ?? '')
    reached.push(href)
  }
  expect(reached).toEqual(expect.arrayContaining(['/gizlilik', '/kullanim-kosullari', 'mailto:destek@kutlio.com']))
})

test('hero primary action navigates to registration within the app', async ({ page }) => {
  await mockApi(page)
  await page.route('**/api/v1/auth/capabilities', route => route.fulfill({ contentType: 'application/json', json: { googleSignInEnabled: false } }))
  await page.goto('/')
  await page.getByRole('region', { name: 'Davetiyeni dakikalar içinde hazırla' }).getByRole('link', { name: 'Ücretsiz başla' }).click()

  await expect(page).toHaveURL(/\/giris\/kayit$/)
  await expect(page.getByRole('heading', { name: 'Hesap oluştur' })).toBeVisible()
})
