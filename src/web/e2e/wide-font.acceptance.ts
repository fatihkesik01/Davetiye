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

// The account drawer and the settings cards use the new choice controls: they must also hold up with wide glyphs, in
// every palette (long names wrap) and in the drawer's scrolling body.
for (const { path, session } of [{ path: '/panel/hesap', session: creator }, { path: '/', session: creator }, { path: '/admin', session: admin }]) {
  for (const spacing of ['0.08em', '0.16em']) {
    test(`the account drawer on ${path} (${session === admin ? 'admin' : 'creator'}) does not overflow with wider font metrics (${spacing})`, async ({ page }, testInfo) => {
      await mock(page, session)
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
      if (testInfo.project.name === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
      await page.addStyleTag({ content: `* { letter-spacing: ${spacing} !important; }` })
      await page.getByRole('banner').getByRole('button', { name: /My account|Hesabım/ }).click()
      const drawer = page.getByRole('dialog')
      await expect(drawer.getByRole('radio', { name: 'Berry' })).toBeEnabled()
      const box = await drawer.evaluate(element => {
        const body = element.querySelector('.preferences-drawer__body') as HTMLElement
        const footer = element.querySelector('.preferences-drawer__footer') as HTMLElement
        return { client: element.clientWidth, scroll: element.scrollWidth, bodyClient: body.clientWidth, bodyScroll: body.scrollWidth, footerScroll: footer.scrollWidth, footerClient: footer.clientWidth }
      })
      expect(box.scroll).toBeLessThanOrEqual(box.client)
      expect(box.bodyScroll).toBeLessThanOrEqual(box.bodyClient)
      expect(box.footerScroll).toBeLessThanOrEqual(box.footerClient)
      const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
      expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
    })
  }
}

for (const colorTheme of ['kutlio', 'sage', 'rose', 'ocean', 'plum'] as const) {
  test(`the settings page cards and choice controls hold up with wide glyphs in the ${colorTheme} palette`, async ({ page }, testInfo) => {
    await mock(page, creator)
    await page.goto('/panel/hesap')
    await expect(page.getByRole('radiogroup', { name: 'Language' })).toBeVisible()
    await page.evaluate(theme => { document.documentElement.dataset.colorTheme = theme }, colorTheme)
    if (testInfo.project.name === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
    await page.addStyleTag({ content: `* { letter-spacing: ${SPACING} !important; }` })
    const overflowing = await page.evaluate(() => [...document.querySelectorAll('.account-card, .choice-group__options, .choice-group__face')].filter(element => element.scrollWidth > element.clientWidth + 1).map(element => element.className))
    expect(overflowing).toEqual([])
    const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
    expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
  })
}
