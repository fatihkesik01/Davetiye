import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const code = 'a'.repeat(64)
const publicPath = `/api/v1/public/invitations/${code}`
const active = {
  status: 'active', templateKey: 'zamansiz-dugun', rendererVersion: 1, contentSchemaVersion: 1,
  content: {
    eventType: 'dugun', headline: 'Yayımlanmış Elif & Deniz', hostNames: ['Elif', 'Deniz'],
    message: 'Yayımlanmış davet mesajımız. Bu özel günü sizinle paylaşmak istiyoruz.',
    startsAt: '2026-10-20T15:00:00Z', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Yayımlanmış bahçe mekânı', address: 'Yayımlanmış özel adres, İstanbul' },
    programItems: [{ title: 'Yayımlanmış karşılama', description: 'Yayımlanmış program', startsAt: '2026-10-20T14:30:00Z' }],
  },
}

test('anonymous public route uses the Published pin and content without auth, management or third-party requests', async ({ page }, testInfo) => {
  const apiRequests: string[] = []
  const externalRequests: string[] = []
  page.on('request', request => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/')) apiRequests.push(url.pathname)
    if (url.origin !== 'http://127.0.0.1:4173') externalRequests.push(url.origin)
  })
  await page.route('**/api/v1/**', async route => {
    if (new URL(route.request().url()).pathname === `${publicPath}/views`) {
      expect(route.request().method()).toBe('POST')
      expect(route.request().headers()['x-invitation-render']).toBe('1')
      return route.fulfill({ status: 204 })
    }
    if (new URL(route.request().url()).pathname === `${publicPath}/memories/configuration`) return route.fulfill({ status: 404 })
    if (new URL(route.request().url()).pathname === '/api/v1/templates') return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    expect(new URL(route.request().url()).pathname).toBe(publicPath)
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(active) })
  })
  await page.goto(`/davetiye/untrusted-decoration-${code}`)
  await applyZoom(page, testInfo.project.name)
  await expect(page.getByRole('heading', { name: active.content.headline })).toBeVisible()
  await expect(page.locator('[data-renderer="zamansiz-dugun@1"]')).toHaveAttribute('data-preview-context', 'public')
  await expect(page.getByText('Saat dilimi: Europe/Istanbul')).toBeVisible()
  await expect(page.getByText(active.content.venue.address)).toBeVisible()
  await expect(page.getByText(/untrusted-decoration/)).toHaveCount(0)
  await expect(page.getByText('Creator Paneli')).toHaveCount(0)
  await expect(page.getByText('Platform yönetimi')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /paylaş|yayınla|güncelle/i })).toHaveCount(0)
  await expect(page).toHaveTitle(`${active.content.headline} | Kutlio`)
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex, nofollow')
  await expectAccessibleAndResponsive(page)
  expect(apiRequests.every(path => path === publicPath || path === `${publicPath}/views` || path === `${publicPath}/memories/configuration` || path === '/api/v1/templates')).toBe(true)
  expect(externalRequests).toEqual([])
  await page.screenshot({ path: testInfo.outputPath('public-active.png'), fullPage: true })
})

test('public cover and gallery media stay responsive and accessible at mobile widths', async ({ page }) => {
  const coverId = '11111111-1111-4111-8111-111111111111'
  const videoId = '22222222-2222-4222-8222-222222222222'
  const snapshot = {
    ...active,
    media: [
      { assetId: coverId, kind: 'Image', role: 'Cover', sortOrder: 0 },
      { assetId: videoId, kind: 'Video', role: 'Gallery', sortOrder: 0 },
    ],
  }
  await page.route('https://media.example.test/**', route => route.fulfill({
    status: 200,
    contentType: 'image/png',
    body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/nX8AAAAASUVORK5CYII=', 'base64'),
  }))
  await page.route('https://stream.example.test/**', route => route.fulfill({
    status: 200, contentType: 'text/html', body: '<!doctype html><html><body><video controls title="Invitation video"></video></body></html>',
  }))
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url())
    if (url.pathname === `${publicPath}/views`) return route.fulfill({ status: 204 })
    if (url.pathname === `${publicPath}/memories/configuration`) return route.fulfill({ status: 404 })
    if (url.pathname === publicPath) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(snapshot) })
    if (url.pathname === '/api/v1/templates') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([{
      key: active.templateKey, name: 'Zamansız Düğün', category: 'Düğün', isPremium: false, rendererVersion: 1,
      previewImageUrl: null, supportedModules: ['hero', 'gallery'], requiredFields: [], recommendedFields: [],
    }]) })
    const assetId = url.pathname.includes(coverId) ? coverId : videoId
    const session = assetId === coverId
      ? { kind: 'image', url: 'https://media.example.test/short/image', expiresAt: new Date(Date.now() + 60_000).toISOString() }
      : { kind: 'video', url: 'https://stream.example.test/short/iframe', expiresAt: new Date(Date.now() + 60_000).toISOString() }
    expect(route.request().method()).toBe('POST')
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(session) })
  })

  await page.goto(`/davetiye/${code}`)
  await expect(page.getByRole('img', { name: 'Davetiye kapak fotoğrafı' })).toBeVisible()
  await expect(page.getByTitle('Davetiye galerisi videosu 1')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
})

for (const next of ['unavailable', 'not-found', 'error'] as const) {
  test(`refresh replaces previously visible personal content with generic ${next}`, async ({ page }, testInfo) => {
    let accessRevoked = false
    let finishRefresh: (() => Promise<void>) | undefined
    await page.route(`**${publicPath}`, async route => {
      if (!accessRevoked) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(active) })
      finishRefresh = () => route.fulfill({ status: next === 'not-found' ? 404 : next === 'error' ? 500 : 200, contentType: 'application/json', body: JSON.stringify(next === 'unavailable' ? { status: 'unavailable' } : {}) })
    })
    await page.goto(`/davetiye/${code}`)
    await applyZoom(page, testInfo.project.name)
    await expect(page.getByRole('heading', { name: active.content.headline })).toBeVisible()
    accessRevoked = true
    await page.evaluate(() => window.dispatchEvent(new Event('focus')))
    await expect(page.getByText(active.content.headline)).toHaveCount(0)
    await expect.poll(() => Boolean(finishRefresh)).toBe(true)
    await finishRefresh!()
    await expect(page.getByRole('heading', { name: next === 'unavailable' ? 'Bu davetiye şu anda yayında değil' : next === 'not-found' ? 'Davetiye bulunamadı' : 'Davetiye yüklenemedi' })).toBeVisible()
    for (const value of [active.content.headline, active.content.venue.name, active.content.venue.address, active.content.programItems[0].title]) await expect(page.getByText(value, { exact: true })).toHaveCount(0)
    await expect(page.locator('.invitation-renderer')).toHaveCount(0)
    await expectGenericMetadata(page)
    await expectAccessibleAndResponsive(page)
    await page.screenshot({ path: testInfo.outputPath(`public-${next}.png`), fullPage: true })
  })
}

test('malformed locator and unavailable pins show generic states without leaking templates or slug data', async ({ page }, testInfo) => {
  let requestCount = 0
  await page.route('**/api/v1/**', route => {
    requestCount++
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...active, templateKey: 'private-missing-pin', rendererVersion: 99 }) })
  })
  await page.goto('/davetiye/creator-id-is-not-public-code')
  await applyZoom(page, testInfo.project.name)
  await expect(page.getByRole('heading', { name: 'Davetiye bulunamadı' })).toBeVisible()
  expect(requestCount).toBe(0)
  await expectGenericMetadata(page)
  await page.goto(`/davetiye/${code}`)
  await applyZoom(page, testInfo.project.name)
  await expect(page.getByRole('heading', { name: 'Davetiye yüklenemedi' })).toBeVisible()
  await expect(page.getByText(/private-missing-pin|şablon sürümü|Yayımlanmış/)).toHaveCount(0)
  await expectAccessibleAndResponsive(page)
})

async function expectGenericMetadata(page: Page) {
  await expect(page).toHaveTitle('Dijital davetiye | Kutlio')
  await expect(page.locator('meta[name="description"]')).toHaveAttribute('content', 'Dijital davetiye sayfası.')
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex, nofollow')
  await expect(page.locator('meta[property^="og:"]')).toHaveCount(0)
}
async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}
async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
