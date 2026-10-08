import { expect, test, type Page } from '@playwright/test'

// Font metrics differ between platforms (Linux CI fonts are wider than Windows). Widen all text deterministically so
// the signed-in and public pages do not depend on one platform's narrower glyphs. Data is intentionally minimal:
// the empty, loading and error states must not overflow either.
const SPACING = '0.16em'
const creator = { authenticated: true, access: 'creator' }
const admin = { authenticated: true, access: 'mfa-complete-super-admin' }
const anonymous = { authenticated: false, access: 'none' }
const preferences = { locale: 'en', colorTheme: 'plum', appearance: 'light', avatar: 'berry' }
const consents = { serviceNotice: { acknowledged: true, acknowledgedAt: '2026-10-01T10:00:00Z', noticeVersion: 'v1', textStatus: 'draft' }, marketing: { optedIn: false, updatedAt: null, version: 'v1' }, history: [] }

const pages: { path: string; session: unknown }[] = [
  { path: '/', session: anonymous }, { path: '/', session: creator },
  { path: '/sablonlar', session: anonymous }, { path: '/giris', session: anonymous }, { path: '/giris/kayit', session: anonymous },
  { path: '/gizlilik', session: anonymous }, { path: '/kullanim-kosullari', session: anonymous },
  { path: '/panel/davetiyeler', session: creator }, { path: '/panel/cop-kutusu', session: creator },
  { path: '/panel/plan-odeme', session: creator }, { path: '/panel/hesap', session: creator },
  { path: '/admin', session: admin }, { path: '/admin/accounts', session: admin }, { path: '/admin/plans', session: admin },
  { path: '/admin/settings', session: admin }, { path: '/admin/templates', session: admin }, { path: '/admin/payments', session: admin }, { path: '/admin/audit', session: admin },
]

async function mock(page: Page, session: unknown) {
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/v1/auth/session') return route.fulfill({ json: session })
    if (path === '/api/v1/account/preferences') return route.fulfill({ json: preferences })
    if (path === '/api/v1/account/consents') return route.fulfill({ json: consents })
    if (path === '/api/v1/antiforgery/token') return route.fulfill({ json: { token: 'csrf' } })
    if (path === '/api/v1/auth/capabilities') return route.fulfill({ json: { googleSignInEnabled: true } })
    if (path === '/api/v1/templates' || path === '/api/v1/public/plans') return route.fulfill({ json: [] })
    return route.fulfill({ status: 503, contentType: 'application/problem+json', json: { status: 503 } })
  })
}

for (const { path, session } of pages) {
  const label = `${path} (${session === anonymous ? 'anonymous' : session === creator ? 'creator' : 'admin'})`
  test(`${label} does not overflow horizontally with wider font metrics`, async ({ page }, testInfo) => {
    await mock(page, session)
    await page.goto(path)
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await page.waitForLoadState('networkidle')
    if (testInfo.project.name === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
    await page.addStyleTag({ content: `* { letter-spacing: ${SPACING} !important; }` })
    const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
    expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
  })
}
