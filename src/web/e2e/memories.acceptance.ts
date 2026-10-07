import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const code = 'b'.repeat(64)
const publicPath = `/api/v1/public/invitations/${code}`
const invitation = {
  status: 'active', templateKey: 'zamansiz-dugun', rendererVersion: 1, contentSchemaVersion: 1,
  content: {
    eventType: 'dugun', headline: 'Anılar Elif & Deniz', hostNames: ['Elif', 'Deniz'],
    message: 'Anılarımızı birlikte biriktirelim. Bu özel günü sizinle paylaşmak istiyoruz.',
    startsAt: '2026-10-20T15:00:00Z', timeZoneId: 'Europe/Istanbul',
    venue: { name: 'Bahçe mekânı', address: 'Özel adres, İstanbul' },
    programItems: [],
  },
}
const limits = { maxDisplayNameCharacters: 60, maxTextCharacters: 500, maxEmojiCharacters: 32 }
const xss = '<img src=x onerror="window.__xss=1"> <b>kalın</b> https://example.com'

interface Mock { submissions: unknown[]; memories: unknown[]; submitStatus: number }

async function mockApi(page: Page, options: { available: boolean; submitStatus?: number }): Promise<Mock> {
  const state: Mock = {
    submissions: [],
    memories: [
      { id: '10000000-0000-4000-8000-000000000001', displayName: 'Ayşe', text: `Harika bir gün!\nİkinci satır ${xss}`, emoji: '🎉', createdAt: '2026-10-05T10:00:00Z', media: [] },
      { id: '10000000-0000-4000-8000-000000000002', displayName: null, text: 'Çok mutlu olduk.', emoji: null, createdAt: '2026-10-05T10:01:00Z', media: [] },
    ],
    submitStatus: options.submitStatus ?? 201,
  }
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url())
    const method = route.request().method()
    if (url.pathname === publicPath) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(invitation) })
    if (url.pathname === `${publicPath}/views`) return route.fulfill({ status: 204 })
    if (url.pathname === '/api/v1/templates') return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    if (url.pathname === `${publicPath}/memories/configuration`) {
      return options.available
        ? route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'available', limits }) })
        : route.fulfill({ status: 404 })
    }
    if (url.pathname === `${publicPath}/memories` && method === 'GET') {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ page: 1, pageSize: 20, totalCount: state.memories.length, items: state.memories }) })
    }
    if (url.pathname === `${publicPath}/memories` && method === 'POST') {
      state.submissions.push(JSON.parse(route.request().postData() ?? '{}'))
      return route.fulfill(state.submitStatus === 201
        ? { status: 201, contentType: 'application/json', body: JSON.stringify({ memoryId: '10000000-0000-4000-8000-000000000003', createdAt: '2026-10-05T10:02:00Z' }) }
        : state.submitStatus === 409
          ? { status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'memory_quota_reached' }) }
          : { status: state.submitStatus })
    }
    if (url.pathname.endsWith('/antiforgery') || url.pathname.includes('antiforgery')) {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ token: 'csrf' }) })
    }
    return route.fulfill({ status: 404 })
  })
  return state
}

test('public memories section lists inert text and accepts a guest memory', async ({ page }, testInfo) => {
  const state = await mockApi(page, { available: true })
  await page.goto(`/davetiye/${code}`)
  await applyZoom(page, testInfo.project.name)

  const heading = page.getByRole('heading', { name: 'Anılarımız' })
  await expect(heading).toBeVisible()
  const list = page.getByRole('list', { name: 'Paylaşılan anılar' })
  await expect(list.getByRole('listitem')).toHaveCount(2)
  await expect(list).toContainText('<img src=x onerror="window.__xss=1">')
  await expect(list.locator('img, b, a')).toHaveCount(0)
  expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined()

  await page.getByLabel('İsim (isteğe bağlı)').fill('Mehmet')
  await page.getByLabel('Notunuz').fill('Çok güzel bir davetiye')
  await expect(page.getByText(/22 \/ 500 birim/)).toBeVisible()
  await page.getByLabel('Emoji (isteğe bağlı)').fill('💐')
  await page.getByRole('button', { name: 'Anıyı gönder' }).click()
  const status = page.getByRole('status').filter({ hasText: 'anınız kaydedildi' })
  await expect(status).toBeVisible()
  await expect(status).toBeFocused()
  expect(state.submissions).toEqual([{ displayName: 'Mehmet', text: 'Çok güzel bir davetiye', emoji: '💐' }])
  await expect(page.getByLabel('Notunuz')).toHaveValue('')

  await expectAccessibleAndResponsive(page)
  await page.screenshot({ path: testInfo.outputPath('public-memories.png'), fullPage: true })
})

test('public memories form validates client-side and is keyboard operable', async ({ page }, testInfo) => {
  await mockApi(page, { available: true })
  await page.goto(`/davetiye/${code}`)
  await applyZoom(page, testInfo.project.name)
  await page.getByLabel('Notunuz').focus()
  await page.getByRole('button', { name: 'Anıyı gönder' }).focus()
  await page.keyboard.press('Enter')
  const summary = page.getByRole('alert').filter({ hasText: 'Göndermeden önce' })
  await expect(summary).toBeFocused()
  await expect(page.getByLabel('Notunuz')).toHaveAttribute('aria-invalid', 'true')
  await expectAccessibleAndResponsive(page)
})

test('public memories shows the quota message and hides entirely when unavailable', async ({ page }, testInfo) => {
  await mockApi(page, { available: true, submitStatus: 409 })
  await page.goto(`/davetiye/${code}`)
  await applyZoom(page, testInfo.project.name)
  await page.getByLabel('Notunuz').fill('Merhaba')
  await page.getByRole('button', { name: 'Anıyı gönder' }).click()
  await expect(page.getByText('anı sınırına ulaşıldı')).toBeVisible()
  await expectAccessibleAndResponsive(page)

  const other = await page.context().newPage()
  await mockApi(other, { available: false })
  await other.goto(`/davetiye/${code}`)
  await expect(other.getByRole('heading', { name: invitation.content.headline })).toBeVisible()
  await expect(other.getByRole('heading', { name: 'Anılarımız' })).toHaveCount(0)
  await expect(other.getByText('Bir anı bırakın')).toHaveCount(0)
})

async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}
async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
