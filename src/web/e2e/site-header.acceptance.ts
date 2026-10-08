import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const STORAGE_KEY = 'kutlio:account-preferences'
const templates = ['zamansiz-dugun', 'romantik-nisan', 'gece-kina'].map((key, index) => ({
  key,
  name: ['Zamansız Düğün', 'Romantik Nişan', 'Gece Kına'][index],
  description: 'Örnek içerikle incelenebilen şablon.',
  category: ['Düğün', 'Nişan', 'Kına'][index],
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
  { ...basePlan, key: 'premium', displayName: 'Premium', priceAmount: 1199, billingPeriod: 'one-time', maxPublishDays: 90, maxRSVPResponses: 1000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true },
]

type Preferences = { locale: 'tr' | 'en'; colorTheme: 'kutlio' | 'sage' | 'rose' | 'ocean' | 'plum'; appearance: 'system' | 'light' | 'dark'; avatar: string | null }
const anonymous = { authenticated: false, access: 'none' }
const creator = { authenticated: true, access: 'creator' }

interface ApiOptions { session?: unknown; preferences?: Preferences | 'unauthorized' }

async function mockApi(page: Page, { session = anonymous, preferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: null } }: ApiOptions = {}) {
  const requests: string[] = []
  const unexpected: string[] = []
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    requests.push(`${request.method()} ${path}`)
    if (path === '/api/v1/auth/session') return route.fulfill({ json: session })
    if (path === '/api/v1/templates') return route.fulfill({ json: templates })
    if (path === '/api/v1/public/plans') return route.fulfill({ json: plans })
    if (path === '/api/v1/auth/capabilities') return route.fulfill({ json: { googleSignInEnabled: false } })
    if (path === '/api/v1/antiforgery/token') return route.fulfill({ json: { token: 'csrf' } })
    if (path === '/api/v1/account/preferences') {
      if (preferences === 'unauthorized') return route.fulfill({ status: 401, contentType: 'application/problem+json', json: { status: 401 } })
      if (request.method() === 'PUT') { preferences = request.postDataJSON() as Preferences }
      return route.fulfill({ json: preferences })
    }
    unexpected.push(`${request.method()} ${path}`)
    return route.fulfill({ status: 404, contentType: 'application/problem+json', json: { status: 404 } })
  })
  return { requests, unexpected }
}

async function seedLocalPreferences(page: Page, preferences: Preferences) {
  await page.addInitScript(([key, value]) => { localStorage.setItem(key, value) }, [STORAGE_KEY, JSON.stringify(preferences)])
}

async function applyZoom(page: Page, projectName: string) {
  if (projectName === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}

async function expectNoHorizontalOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
}

async function expectAccessible(page: Page) {
  const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
  expect(accessibility.violations).toEqual([])
}

const publicPages = [
  { path: '/', ready: 'Davetiyeni dakikalar içinde hazırla' },
  { path: '/sablonlar', ready: 'Şablonlar' },
  { path: '/giris', ready: 'Giriş yap' },
  { path: '/gizlilik', ready: 'Hizmet bildirimi ve gizlilik' },
  { path: '/kullanim-kosullari', ready: 'Kullanım koşulları' },
] as const

async function waitForPage(page: Page, ready: string) {
  await expect(page.getByRole('heading', { level: 1 }).first()).toContainText(ready)
}

test('anonymous visitors see the same bar everywhere with a sign-in link and no account controls', async ({ page }) => {
  const { requests, unexpected } = await mockApi(page)
  const bars: { brand: string | null; links: string[]; height: number; brandBox: { x: number; y: number; width: number }; font: string }[] = []

  for (const { path, ready } of publicPages) {
    await page.goto(path)
    await waitForPage(page, ready)
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('link', { name: 'Giriş yap' })).toHaveAttribute('href', '/giris')
    await expect(banner.getByRole('button')).toHaveCount(0)
    await expect(banner.getByRole('link', { name: 'Panele git' })).toHaveCount(0)
    const brand = banner.getByRole('link', { name: 'Kutlio ana sayfa' })
    const box = await brand.boundingBox()
    bars.push({
      brand: await brand.textContent(),
      links: await banner.getByRole('navigation', { name: 'Ana gezinme' }).getByRole('link').allTextContents(),
      height: (await banner.boundingBox())?.height ?? 0,
      brandBox: { x: box?.x ?? 0, y: box?.y ?? 0, width: box?.width ?? 0 },
      font: await brand.evaluate(element => { const style = getComputedStyle(element); return `${style.fontFamily}|${style.fontSize}|${style.fontWeight}` }),
    })
  }

  // The landing adds a second, in-page section navigation, so its bar may be taller on narrow screens; its first row still lines up.
  for (const [index, bar] of bars.entries()) {
    expect(bar.brand).toBe('Kutlio')
    expect(bar.links).toEqual(['Şablonlar'])
    if (index > 0) expect(Math.abs(bar.height - bars[1].height)).toBeLessThanOrEqual(1)
    expect(Math.abs(bar.brandBox.x - bars[0].brandBox.x)).toBeLessThanOrEqual(1)
    expect(Math.abs(bar.brandBox.y - bars[0].brandBox.y)).toBeLessThanOrEqual(1)
    expect(bar.font).toBe(bars[0].font)
  }
  expect(requests.filter(request => request === 'GET /api/v1/account/preferences')).toHaveLength(0)
  expect(unexpected).toEqual([])
})

test('a signed-in Creator keeps the signed-in bar and the chosen theme on the landing, catalog, auth and legal pages', async ({ page }) => {
  const { requests, unexpected } = await mockApi(page, { session: creator, preferences: { locale: 'tr', colorTheme: 'sage', appearance: 'dark', avatar: null } })

  for (const { path, ready } of publicPages) {
    await page.goto(path)
    await waitForPage(page, ready)
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('link', { name: 'Panele git' })).toHaveAttribute('href', '/panel/davetiyeler')
    await expect(banner.getByRole('button', { name: 'Hesabım, tema ve dil' })).toBeVisible()
    await expect(banner.getByRole('link', { name: 'Giriş yap' })).toHaveCount(0)
    await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'sage')
    await expect(page.locator('html')).toHaveAttribute('data-appearance', 'dark')
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--account-accent').trim())).toBe('#3f6654')
    await expect(banner).not.toContainText('@')
  }
  expect(requests.filter(request => request === 'GET /api/v1/auth/session').length).toBe(publicPages.length)
  expect(unexpected).toEqual([])
})

test('the theme and language follow client-side navigation between the landing, catalog and legal pages', async ({ page }) => {
  const { requests, unexpected } = await mockApi(page, { session: creator, preferences: { locale: 'en', colorTheme: 'rose', appearance: 'light', avatar: null } })
  await page.goto('/')
  await expect(page.getByRole('banner').getByRole('link', { name: 'Go to dashboard' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'rose')

  await page.getByRole('banner').getByRole('link', { name: 'Templates' }).click()
  await expect(page).toHaveURL(/\/sablonlar$/)
  await expect(page.getByRole('banner').getByRole('link', { name: 'Go to dashboard' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'rose')
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await page.getByRole('banner').getByRole('link', { name: 'Kutlio home' }).click()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('banner').getByRole('link', { name: 'Go to dashboard' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'rose')
  // One session request for the whole page load, however many client-side routes were visited.
  expect(requests.filter(request => request === 'GET /api/v1/auth/session')).toHaveLength(1)
  expect(unexpected).toEqual([])
})

test('a palette chosen from the account drawer on the landing persists on the catalog', async ({ page }) => {
  const { unexpected } = await mockApi(page, { session: creator })
  await page.goto('/')
  await page.getByRole('banner').getByRole('button', { name: 'Hesabım, tema ve dil' }).click()
  const drawer = page.getByRole('dialog')
  await expect(drawer.getByRole('button', { name: 'Okyanus' })).toBeEnabled()
  await drawer.getByRole('button', { name: 'Okyanus' }).click()
  await drawer.getByRole('button', { name: 'Koyu' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'ocean')
  await expect(page.locator('html')).toHaveAttribute('data-appearance', 'dark')
  await expect(drawer.getByRole('status')).toHaveText('Tercihleriniz kaydedildi.')
  await drawer.getByRole('button', { name: 'Kapat' }).click()

  await page.getByRole('banner').getByRole('link', { name: 'Şablonlar' }).click()
  await expect(page).toHaveURL(/\/sablonlar$/)
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'ocean')
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('kutlio:account-preferences') ?? 'null'))).toMatchObject({ colorTheme: 'ocean', appearance: 'dark', avatar: null })
  expect(unexpected).toEqual([])
})

test('an expired session clears the previous user preference from the browser', async ({ page }) => {
  await seedLocalPreferences(page, { locale: 'en', colorTheme: 'plum', appearance: 'dark', avatar: null })
  const { unexpected } = await mockApi(page, { session: creator, preferences: 'unauthorized' })
  await page.goto('/')
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'kutlio')
  await expect(page.locator('html')).toHaveAttribute('data-appearance', 'system')
  expect(await page.evaluate(() => localStorage.getItem('kutlio:account-preferences'))).toBeNull()
  expect(unexpected).toEqual([])
})

test('the public invitation page has no site header, no session lookup and no account theme', async ({ page }) => {
  const code = 'b'.repeat(64)
  const requests: string[] = []
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    requests.push(path)
    if (path === `/api/v1/public/invitations/${code}`) return route.fulfill({ json: {
      status: 'active', templateKey: 'zamansiz-dugun', rendererVersion: 1, contentSchemaVersion: 1,
      content: { eventType: 'dugun', headline: 'Yalıtılmış Davet', hostNames: ['Elif', 'Deniz'], message: 'Mesaj', startsAt: '2026-10-20T15:00:00Z', timeZoneId: 'Europe/Istanbul', venue: { name: 'Mekan', address: 'Adres' }, programItems: [] },
    } })
    if (path === `/api/v1/public/invitations/${code}/views`) return route.fulfill({ status: 204 })
    if (path === `/api/v1/public/invitations/${code}/memories/configuration`) return route.fulfill({ status: 404 })
    if (path === '/api/v1/templates') return route.fulfill({ json: [] })
    return route.fulfill({ status: 500, json: { unexpected: path } })
  })
  await page.goto(`/davetiye/${code}`)
  await expect(page.getByRole('heading', { name: 'Yalıtılmış Davet' })).toBeVisible()
  await expect(page.getByRole('banner')).toHaveCount(0)
  await expect(page.locator('[data-site-header]')).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Kutlio ana sayfa' })).toHaveCount(0)
  await expect(page.locator('html')).toHaveAttribute('data-color-theme', 'kutlio')
  await expect(page.locator('html')).toHaveAttribute('data-appearance', 'system')
  expect(requests).not.toContain('/api/v1/auth/session')
  expect(requests).not.toContain('/api/v1/account/preferences')
})

const themeMatrix: { name: string; preferences: Preferences }[] = [
  { name: 'kutlio light', preferences: { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: null } },
  { name: 'kutlio dark', preferences: { locale: 'tr', colorTheme: 'kutlio', appearance: 'dark', avatar: null } },
  { name: 'sage light', preferences: { locale: 'tr', colorTheme: 'sage', appearance: 'light', avatar: null } },
  { name: 'rose dark', preferences: { locale: 'tr', colorTheme: 'rose', appearance: 'dark', avatar: null } },
  { name: 'ocean light', preferences: { locale: 'en', colorTheme: 'ocean', appearance: 'light', avatar: null } },
  { name: 'plum dark', preferences: { locale: 'en', colorTheme: 'plum', appearance: 'dark', avatar: null } },
]

for (const { name, preferences } of themeMatrix) {
  for (const authenticated of [false, true]) {
    test(`landing, catalog and legal pages are accessible without overflow in ${name} (${authenticated ? 'signed in' : 'anonymous'})`, async ({ page }, testInfo) => {
      await seedLocalPreferences(page, preferences)
      const { unexpected } = await mockApi(page, { session: authenticated ? creator : anonymous, preferences })
      for (const path of ['/', '/sablonlar', '/giris', '/gizlilik', '/kullanim-kosullari']) {
        await page.goto(path)
        await applyZoom(page, testInfo.project.name)
        // Generous timeout: this loop shares a cold server with many parallel workers on loaded runners.
        await expect(page.locator('html')).toHaveAttribute('data-color-theme', preferences.colorTheme, { timeout: 20_000 })
        await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
        if (path === '/' || path === '/sablonlar') await expect(page.locator('img[src^="/template-previews/"]').first()).toBeVisible()
        if (authenticated) await expect(page.getByRole('banner').getByRole('button', { name: /Hesabım|My account/ })).toBeVisible()
        else await expect(page.getByRole('banner').getByRole('link', { name: /Giriş yap|Sign in/ })).toBeVisible()
        await expectNoHorizontalOverflow(page)
        await expectAccessible(page)
        if (path === '/') {
          await page.locator('.landing-faq__item').first().evaluate(element => { (element as HTMLDetailsElement).open = true })
          await expectAccessible(page)
        }
      }
      expect(unexpected).toEqual([])
    })
  }
}

test('the bar stays on one tidy row-set at narrow widths for every session state', async ({ page }) => {
  for (const session of [anonymous, creator, { authenticated: true, access: 'mfa-setup-required-super-admin' }, { authenticated: true, access: 'mfa-complete-super-admin' }]) {
    await mockApi(page, { session })
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByRole('banner').getByRole('link', { name: 'Giriş yap' })).toHaveCount(session.authenticated ? 0 : 1, { timeout: 10_000 })
    await expectNoHorizontalOverflow(page)
    await page.unrouteAll({ behavior: 'ignoreErrors' })
  }
})
