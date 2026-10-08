import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const entitlementKeys = [
  'maxPublishDays', 'maxActiveInvitations', 'maxImages', 'maxVideos', 'maxImageSizeMb', 'maxVideoSizeMb',
  'maxVideoDurationSeconds', 'maxGuestImages', 'maxGuestVideos', 'maxGuestImageSizeMb', 'maxGuestVideoSizeMb',
  'maxGuestVideoDurationSeconds', 'maxRSVPResponses', 'memoriesEnabled', 'giftRegistryEnabled', 'premiumTemplatesEnabled',
]
const freePlan = {
  id: '11111111-1111-4111-8111-111111111111', key: 'free', displayName: 'Ücretsiz', description: null,
  priceAmount: 0, currency: 'TRY', billingKind: 'Free', revision: 1,
  entitlements: entitlementKeys.map(key => ({
    key,
    numericValue: key === 'memoriesEnabled' || key === 'giftRegistryEnabled' || key === 'premiumTemplatesEnabled' ? null : 0,
    booleanValue: key === 'memoriesEnabled' || key === 'giftRegistryEnabled' || key === 'premiumTemplatesEnabled' ? false : null,
  })),
}
const overview = {
  generatedAtUtc: '2026-10-06T10:00:00Z',
  accounts: { total: 0, individual: 0, organization: 0, banned: 0 },
  invitations: { draft: 0, scheduled: 0, active: 0, paused: 0, expired: 0, deleted: 0 },
  plans: { total: 1, active: 1, inactive: 0 },
  grants: { total: 0, free: 0, individualPurchase: 0, organizationSubscription: 0, revoked: 0 },
  payments: { pending: 0, unknown: 0, succeeded: 0, failed: 0, canceled: 0, reversed: 0 },
  storage: { assets: 0, ready: 0, pendingUpload: 0, processing: 0, pendingDeletion: 0, deleted: 0, rejected: 0, verifiedBytes: 0 },
  health: { api: 'Healthy', database: 'Healthy' },
}

test('MFA-complete Admin can navigate overview and read-only operational/configuration routes', async ({ page }, testInfo) => {
  const requested: string[] = []
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url())
    const path = url.pathname
    requested.push(`${route.request().method()} ${path}${url.search}`)
    if (path === '/api/v1/account/preferences') return json(route, { locale: 'tr', colorTheme: 'kutlio', appearance: 'system' })
    if (path === '/api/v1/auth/session') return json(route, { authenticated: true, access: 'mfa-complete-super-admin' })
    if (path === '/api/v1/antiforgery/token') return json(route, { token: 'admin-csrf' })
    if (path === '/api/v1/admin/overview') return json(route, overview)
    if (path === '/api/v1/admin/accounts/banned/search') {
      expect(route.request().method()).toBe('POST')
      expect(route.request().headers()['x-csrf-token']).toBe('admin-csrf')
      return json(route, { items: [], page: 1, pageSize: 50, totalCount: 0 })
    }
    if (path === '/api/v1/admin/plans') return json(route, [freePlan])
    if (path === '/api/v1/admin/settings') return json(route, { items: [
      { key: 'deletedInvitationRetentionDays', displayName: 'Silinen davetiyelerin saklama süresi', description: 'Çöp kutusu saklama süresi.', value: 30, minimum: 0, maximum: 365, revision: 1 },
      { key: 'abandonedMemoryRetentionDays', displayName: 'Tamamlanmamış anı kayıtlarının saklama süresi', description: 'Terk edilmiş kayıtların saklama süresi.', value: 7, minimum: 0, maximum: 365, revision: 1 },
    ] })
    if (path === '/api/v1/admin/templates') return json(route, [{
      id: '22222222-2222-4222-8222-222222222222', key: 'minimal-acilis', name: 'Minimal Açılış', description: null, isActive: true, revision: 1,
    }])
    if (path === '/api/v1/admin/payments' || path === '/api/v1/admin/audit') {
      return json(route, { items: [], page: 1, pageSize: 50, totalCount: 0 })
    }
    throw new Error(`Unexpected Admin E2E API request: ${route.request().method()} ${path}`)
  })

  await page.goto('/admin')
  await applyZoom(page, testInfo.project.name)
  await expect(page.getByRole('heading', { name: 'Platform özeti' })).toBeVisible()
  await expect(page.getByText('Yalnızca toplu operasyon göstergeleri. Özel davetli içerikleri bu panelde gösterilmez.')).toBeVisible()
  await expectAccessibleAndResponsive(page)

  const routes = [
    { link: 'Banlı hesaplar', heading: 'Banlı hesaplar', content: 'E-posta başlangıcı' },
    { link: 'Planlar ve haklar', heading: 'Planlar ve haklar', content: 'Ücretsiz' },
    { link: 'Sistem ayarları', heading: 'Sistem ayarları', content: 'Saklama süresi' },
    { link: 'Şablonlar', heading: 'Şablonlar', content: 'Minimal Açılış' },
    { link: 'Ödemeler', heading: 'Ödemeler', content: 'Henüz kayıt yok' },
    { link: 'Denetim kayıtları', heading: 'Denetim kayıtları', content: 'Henüz kayıt yok' },
  ]

  for (const item of routes) {
    await page.getByRole('link', { name: item.link, exact: true }).click()
    await expect(page.getByRole('heading', { name: item.heading, exact: true })).toBeVisible()
    await expect(page.getByText(item.content, { exact: false }).first()).toBeVisible()
    await expect(page.getByRole('link', { name: item.link, exact: true })).toHaveAttribute('aria-current', 'page')
    await expectAccessibleAndResponsive(page)
  }

  expect(requested).toContain('GET /api/v1/admin/overview')
  expect(requested).toContain('POST /api/v1/admin/accounts/banned/search')
  expect(requested).toContain('GET /api/v1/admin/plans')
  expect(requested).toContain('GET /api/v1/admin/settings')
  expect(requested).toContain('GET /api/v1/admin/templates')
  expect(requested.some(value => value.startsWith('GET /api/v1/admin/payments?'))).toBe(true)
  expect(requested.some(value => value.startsWith('GET /api/v1/admin/audit?'))).toBe(true)
  expect(requested.some(value => value.startsWith('PUT ') || value.includes('/unban'))).toBe(false)
})

function json(route: import('@playwright/test').Route, value: unknown) {
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) })
}

async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}

async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
